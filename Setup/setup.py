# -*- coding: utf-8 -*-

import sys
import os
import shutil
import glob
import fnmatch
import codecs
import re
from lxml import etree


def get_buildtoolkit_path():
    return os.path.abspath(os.path.join(os.path.split(__file__)[0], ".."))


def show_usage():
    print "[Usage] setup.py command"
    print "Command:"
    print "   INIT path [-b] Copy initial files to path"
    print "   RULE path      Configure project to have analysis rule recursively"
    print "   LICENSE path   Configure project to have same license recursively"


def init():
    src_path = get_buildtoolkit_path()
    dst_path = sys.argv[-1]
    include_build = (len(sys.argv) > 3 and sys.argv[2].lower() == "-b")

    shutil.copy(src_path + "/.gitattributes", dst_path)
    shutil.copy(src_path + "/.gitignore", dst_path)
    shutil.copy(src_path + "/CodeStyle/.editorconfig", dst_path)
    shutil.copy(src_path + "/CodeStyle/CodeAnalysis.ruleset", dst_path)

    if include_build:
        shutil.copy(src_path + "/BuildScript/build.cmd", dst_path)
        if not os.path.exists(dst_path + "/build.fsx"):
            shutil.copy(src_path + "/BuildScript/build.fsx", dst_path)

    for f in glob.glob(dst_path + "/*.DotSettings"):
        os.remove(f)
    if os.path.exists(dst_path + "/Settings.StyleCop"):
        os.remove(dst_path + "/Settings.StyleCop")


def load_proj(path):
    with codecs.open(path, encoding="utf-8") as f:
        proj_text = f.read()
    proj_text = proj_text.replace("xmlns=", "__xmlns__=")
    return etree.fromstring(proj_text)


def save_proj(path, proj):
    proj_text = etree.tostring(proj, encoding="utf-8")
    t = proj_text.replace("__xmlns__=", "xmlns=").replace("/>", " />").replace("\n", "\r\n")
    with codecs.open(path, "w", encoding="utf-8-sig") as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>\r\n')
        f.write(t)


def add_element(element, child):
    # add child element to element while keeping identation (>_<)
    list(element)[-1].tail += "  "
    child.tail = element.text[0: -2]
    element.append(child)
    
    
def rule():
    dst_path = sys.argv[2]
    for root, dirnames, filenames in os.walk(dst_path):
        for filename in fnmatch.filter(filenames, '*.csproj'):
            path = os.path.join(root, filename)
            print path
            try:            
                proj = load_proj(path)
                pgroup_base = [e for e in proj if e.tag == "PropertyGroup" and len(e.keys()) == 0][0]
                pgroup_rel = [e for e in proj if e.tag == "PropertyGroup" and e.get("Condition") != None and e.get("Condition").find("Release") != -1][0]
                ruleset_relpath = os.path.relpath(dst_path, root) + "\CodeAnalysis.ruleset"
                add_element(pgroup_base, etree.XML('<CodeAnalysisRuleSet>' + ruleset_relpath + '</CodeAnalysisRuleSet>'))
                add_element(pgroup_rel, etree.XML('<RunCodeAnalysis>true</RunCodeAnalysis>'))
                save_proj(path, proj)
            except Exception as e:
                print "!", e


def open_replace_save(path, transform):
    with codecs.open(path, encoding="utf-8") as f:
        text = f.read()
    text = transform(text)
    with codecs.open(path, "w", encoding="utf-8") as f:
        f.write(text)


def license():
    dst_path = sys.argv[2]
    for root, dirnames, filenames in os.walk(dst_path):
        for filename in fnmatch.filter(filenames, 'LICENSE'):
            path = os.path.join(root, filename)
            print path
            open_replace_save(path, lambda x: re.sub(r"Copyright \(c\) .*", "Copyright (c) 2016 SaladLab", x))
            
        for filename in fnmatch.filter(filenames, 'AssemblyInfo.cs'):
            path = os.path.join(root, filename)
            print path
            open_replace_save(path, lambda x:
                              re.sub(r"\[assembly: AssemblyCopyright(.*)\]", u'[assembly: AssemblyCopyright("Copyright © 2016 SaladLab")]',
                                  re.sub(r"\[assembly: AssemblyCompany.*\]", u'[assembly: AssemblyCompany("SaladLab")]', x)))
        
        for filename in fnmatch.filter(filenames, '*.nuspec'):
            path = os.path.join(root, filename)
            print path
            open_replace_save(path, lambda x: re.sub(r"\<copyright\>.*\</copyright\>", u'<copyright>Copyright © 2016 SaladLab</copyright>', x))


def main():
    if len(sys.argv) <= 1:
        show_usage()
        return

    cmd = sys.argv[1].lower()
    if cmd == "init":
        return init()
    elif cmd == "rule":
        rule()
    elif cmd == "license":
        license()
    else:
        print "Wrong command: " + cmd
        sys.exit(1)
    
    print get_buildtoolkit_path()


if __name__ == "__main__":
    main()
