#!/usr/bin/python

import os
import stat
import shutil
import sys
import tarfile
import urllib2
import json
import codecs
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
    return (int(x["major"]), int(x["minor"]), int(x["patch"]), x["prerelease"] or "\xff")


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
    if ver is None or ver == "" or ver == "latest":
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


def make_filter(nosample, includes, excludes):
    if nosample:
        if includes or excludes:
            raise Exception("nosample cannot used with includes or excludes")
        return lambda p: os.path.split(p)[0].lower().find("sample") == -1

    if includes:
        if excludes:
            raise Exception("includes cannot used with excludes")
        return lambda p: any(re.match(pattern, p) for pattern in includes)

    if excludes:
        if includes:
            raise Exception("excludes cannot used with includes")
        print excludes
        return lambda p: all((re.match(pattern, p) is None) for pattern in excludes)

    return None


def install_package(id, version, target, nosample=None, includes=None, excludes=None, verbose=False):
    print "* download package: {0}(version={1})".format(id, version or "latest")
    package = download_github_releases(id, version, verbose=verbose)
    print "saved: {0}".format(package)

    print "* install package to {0}".format(target)
    extract_unitypackage(package, target, make_filter(nosample, includes, excludes), verbose=verbose)


def run_for_package(args):
    install_package(args.id, args.version, args.nosample, args.include, args.exclude, verbose=True)


def run_for_package_config(args):
    j = json.load(codecs.open(args.id))
    if "dependencies" not in j:
        print "No dependencies"
        return
    target = os.path.split(args.id)[0]
    for id, body in j["dependencies"].iteritems():
        if isinstance(body, str) or isinstance(body, unicode):
            install_package(id, body, target, verbose=True)
        else:
            ver = body.get("version", None)
            nosample = body.get("nosample", False)
            includes = body.get("includes", None)
            excludes = body.get("excludes", None)
            install_package(id, ver, target, nosample, includes, excludes, verbose=True)


def run(args):
    if args.packageconfig:
        run_for_package_config(args)
    else:
        run_for_package(args)

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
    parser.add_argument("--packageconfig", action='store_true', help="Use package config file")

    # example: "SaladLab/Unity3D.UiManager --include .*Sample.* --include .*Handle.*".split()
    # example: "unitypackage.test.json --packageconfig".split()
    run(parser.parse_args())


if __name__ == "__main__":
    main()
