#!/usr/bin/python

import os
import stat
import shutil
import sys
import tarfile
import urllib2
import json
import re
import tempfile
import argparse


re_semver = re.compile('(?P<major>(?:0|[1-9][0-9]*))'
                      '\.(?P<minor>(?:0|[1-9][0-9]*))'
                      '\.(?P<patch>(?:0|[1-9][0-9]*))'
                      '(\-(?P<prerelease>[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*))?'
                      '(\+(?P<build>[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*))?')


def search_semver(str):
    mo = re_semver.search(str)
    if mo is None: return None
    return str[mo.start(0):mo.end(0)]


def parse_semver(str):
    mo = re_semver.match(str)
    if mo is None: return None
    d = mo.groupdict()
    return d


def parse_semver_for_key(str):
    x = parse_semver(str)
    return (x["major"], x["minor"], x["patch"], x["prerelease"] or "\xff")


def get_github_releases(id):
    response = urllib2.urlopen('https://api.github.com/repos/' + id + '/releases')
    response_json = json.load(response)
    releases = dict()
    for release in response_json:
        ver = search_semver(release['tag_name']) or search_semver(release['name'])
        if ver:
            releases[ver] = release
    return releases


def get_package_rootpath():
    return os.path.join(os.environ["APPDATA"], "unityget")


def download_github_releases(id, ver, force=False, verbose=False):
    # get release information for package
    d = get_github_releases(id)
    if ver is None:
        ver = max(d.keys(), key=parse_semver_for_key)
        if verbose:
            print "detect version:", ver
    if ver not in d:
        raise Exception("Specified version is not found.")
    for asset in d[ver]["assets"]:
        download_url = asset["browser_download_url"]
        if os.path.splitext(download_url)[1].lower() == ".unitypackage":
            url = download_url
            break
    if url is None:
        raise Exception("No unitypackage in release.")

    # save unitypackage into local cache folder
    if not os.path.exists(get_package_rootpath()):
        os.makedirs(get_package_rootpath())
    savefile = (id + "." + ver).replace("/", "_") + ".unitypackage"
    savepath = os.path.join(get_package_rootpath(), savefile)
    if force or not os.path.exists(savepath):
        if verbose:
            print "download:", url
        response = urllib2.urlopen(url)
        data = response.read()
        with open(savepath, "wb") as f:
            f.write(data)
    return savepath


# this function is based on https://github.com/gered/extractunitypackage/blob/master/extractunitypackage.py

def extract_unitypackage(package, target, filter=None, verbose=False):
    # extract .unitypackage contents to a temporary directory
    tempDir = tempfile.mkdtemp()
    tar = tarfile.open(package, 'r:gz')
    tar.extractall(tempDir);
    tar.close()

    # build association between the unitypackage's root directory names
    # (which each have 1 asset in them) to the actual filename (stored in the 'pathname' file)
    mapping = {}
    for i in os.listdir(tempDir):
        rootFile = os.path.join(tempDir, i)
        asset = i

        if os.path.isdir(rootFile):
            realPath = ''

            # we need to check if an 'asset' file exists (sometimes it won't be there
            # such as when the 'pathname' file is just specifying a directory)
            hasAsset = False

            for j in os.listdir(rootFile):
                # grab the real path
                if j == 'pathname':
                    lines = [line.strip() for line in open(os.path.join(rootFile, j))]
                    realPath = lines[0]     # should always be on the first line
                elif j == 'asset':
                    hasAsset = True

            # if an 'asset' file exists in this directory, then this directory
            # contains a file that should be moved+renamed. otherwise we can
            # ignore this directory altogether...
            if hasAsset:
                mapping[asset] = realPath

    # mapping from unitypackage internal filenames to real filenames is now built
    # walk through them all and move the 'asset' files out and rename, building
    # the directory structure listed in the real filenames we found as we go
    for asset, asset_path in mapping.iteritems():
        if filter:
            if filter(asset_path):
                if verbose:
                    print "copy:", asset_path
            else:
                if verbose:
                    print "skip:", asset_path
                continue
        else:
            if verbose:
                print "copy:", asset_path

        srcFile = os.path.join(tempDir, asset, 'asset');
        srcFileMeta = srcFile + ".meta"

        path, filename = os.path.split(asset_path)
        destDir = os.path.join(target, path)
        destFile = os.path.join(destDir, filename)
        destFileMeta = destFile + ".meta"

        if not os.path.exists(destDir):
            os.makedirs(destDir)

        if os.path.exists(destFile):
            os.remove(destFile)
        shutil.move(srcFile, destFile)

        if os.path.exists(destFileMeta):
            os.remove(destFileMeta)
        shutil.move(srcFileMeta, destFileMeta)
    
    shutil.rmtree(tempDir)


def make_filter(args):
    if args.nosample:
        if args.include or args.exclude:
            raise Exception("nosample cannot used with include or exclude")
        return lambda p: os.path.split(p)[0].lower().find("sample") == -1

    if args.include:
        if args.exclude:
            raise Exception("include cannot used with exclude")
        return lambda p: any(re.match(pattern, p) for pattern in args.include)

    if args.exclude:
        if args.include:
            raise Exception("exclude cannot used with include")
        print args.exclude
        return lambda p: all((re.match(pattern, p) is None) for pattern in args.exclude)

    return None


def run(args):
    print
    print "* download package: {0}(version={1})".format(args.id, args.version or "latest")
    print
    package = download_github_releases(args.id, args.version, verbose=True)
    print "saved: {0}".format(package)

    print
    print "* install package to {0}".format(args.target)
    print
    extract_unitypackage(package, args.target, make_filter(args), verbose=True)

    print
    print "done"


def main():
    print "UnityGet 0.1"
    parser = argparse.ArgumentParser()
    parser.add_argument("id", help="Package ID (like SaladLab/Unity3D.UiManager) or Package File.")
    parser.add_argument("--version", help="Package version to install. (default: latest)")
    parser.add_argument("--target", default = "./")
    parser.add_argument("--nosample", action='store_true', help="Exclude all files in sample directories")
    parser.add_argument("--include", action='append', help="Regular expression filter to include files")
    parser.add_argument("--exclude", action='append', help="Regular expression filter to exclude files")
    parser.add_argument("--packagefile", action='store_true', help="Use package file")

    # example: "SaladLab/Unity3D.UiManager --include .*Sample.* --include .*Handle.*".split()
    # example: "./TestUnityProject/unityget.packages.config --packagefile
    run(parser.parse_args())


if __name__ == "__main__":
    main()
