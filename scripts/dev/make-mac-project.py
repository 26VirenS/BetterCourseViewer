#!/usr/bin/env python3
"""Writes mac/Simpl.xcodeproj — the native Mac app's Xcode project — without Xcode or XcodeGen.

    python3 scripts/dev/make-mac-project.py

The Mac app's own sources live in mac/Simpl/, a folder Xcode keeps in step by itself (a synchronized
folder: a Swift file or an asset added there is in the app, no project edit). What it shares with the
iPhone app is named here, file by file (SHARED below): the engine's web layer, the models the page's
answers decode into, the router, the quiz's run, reminders and new-activity alerts. Each of those files
builds for both platforms (#if os(iOS) where they differ). The extension/ folder is bundled whole, as in
the iPhone app, and bridge.js and login.js beside it.

IDs are derived from names, so running this again writes the same project (CI checks that the project
committed is the one this writes)."""
import hashlib
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
MAC = os.path.join(ROOT, 'mac')
PROJ = os.path.join(MAC, 'Simpl.xcodeproj')

NAME = 'Simpl'
BUNDLE_ID = 'com.simplcourses.mac'
MARKETING_VERSION = '1.2.4' # Simpl for Mac's own version line (scripts/release-mac-native.sh releases it as mac-v<this>)
BUILD = '7' # raised with every release
DEPLOYMENT = '14.0'

# The iPhone app's files the Mac app builds too (paths under ios/SimplCourses).
SHARED = {
    'App': ['AppSession.swift', 'SchoolPickerView.swift'],
    'Native': ['Models.swift', 'CourseModels.swift', 'QuizModels.swift', 'QuizRun.swift', 'Reminders.swift', 'Activity.swift', 'Router.swift'],
    'Web': ['Bridge.swift', 'BridgeStore.swift', 'ContentRules.swift', 'CookieJar.swift', 'LoginAssist.swift', 'ScriptBundle.swift', 'WebController.swift'],
}
SHARED_RESOURCES = {'Web': ['bridge.js', 'login.js']}


def oid(*parts):
    return hashlib.sha1(('simpl-mac/' + '/'.join(parts)).encode()).hexdigest()[:24].upper()


def q(s):
    """A pbxproj string: bare when it can be, quoted otherwise."""
    import re
    if re.fullmatch(r'[A-Za-z0-9_./$]+', s) and not s.startswith('/'):
        return s
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


lines = []
w = lines.append

ids = {
    'project': oid('project'),
    'main': oid('group', 'main'),
    'products': oid('group', 'products'),
    'app': oid('product', NAME),
    'synced': oid('synced', 'Simpl'),
    'support': oid('group', 'Support'),
    'infoplist': oid('file', 'Support/Info.plist'),
    'shared': oid('group', 'Shared'),
    'repo': oid('group', 'repo'),
    'extension': oid('file', 'extension'),
    'extension_build': oid('build', 'extension'),
    'target': oid('target', NAME),
    'sources': oid('phase', 'sources'),
    'resources': oid('phase', 'resources'),
    'frameworks': oid('phase', 'frameworks'),
    'target_configs': oid('configlist', 'target'),
    'project_configs': oid('configlist', 'project'),
    'target_debug': oid('config', 'target', 'Debug'),
    'target_release': oid('config', 'target', 'Release'),
    'project_debug': oid('config', 'project', 'Debug'),
    'project_release': oid('config', 'project', 'Release'),
}

shared_files = []  # (group, name, file id, build id, kind)
for group, names in SHARED.items():
    for n in names:
        shared_files.append((group, n, oid('file', 'shared', group, n), oid('build', 'shared', group, n), 'swift'))
for group, names in SHARED_RESOURCES.items():
    for n in names:
        shared_files.append((group, n, oid('file', 'shared', group, n), oid('build', 'shared', group, n), 'js'))

w('// !$*UTF8*$!')
w('{')
w('\tarchiveVersion = 1;')
w('\tclasses = {')
w('\t};')
w('\tobjectVersion = 77;')
w('\tobjects = {')
w('')

w('/* Begin PBXBuildFile section */')
for group, n, fid, bid, kind in shared_files:
    phase = 'Sources' if kind == 'swift' else 'Resources'
    w(f'\t\t{bid} /* {n} in {phase} */ = {{isa = PBXBuildFile; fileRef = {fid} /* {n} */; }};')
w(f'\t\t{ids["extension_build"]} /* extension in Resources */ = {{isa = PBXBuildFile; fileRef = {ids["extension"]} /* extension */; }};')
w('/* End PBXBuildFile section */')
w('')

w('/* Begin PBXFileReference section */')
w(f'\t\t{ids["app"]} /* {NAME}.app */ = {{isa = PBXFileReference; explicitFileType = wrapper.application; includeInIndex = 0; path = {q(NAME + ".app")}; sourceTree = BUILT_PRODUCTS_DIR; }};')
w(f'\t\t{ids["infoplist"]} /* Info.plist */ = {{isa = PBXFileReference; lastKnownFileType = text.plist.xml; path = Info.plist; sourceTree = "<group>"; }};')
w(f'\t\t{ids["extension"]} /* extension */ = {{isa = PBXFileReference; lastKnownFileType = folder; name = extension; path = ../extension; sourceTree = SOURCE_ROOT; }};')
for group, n, fid, bid, kind in shared_files:
    ftype = 'sourcecode.swift' if kind == 'swift' else 'sourcecode.javascript'
    w(f'\t\t{fid} /* {n} */ = {{isa = PBXFileReference; lastKnownFileType = {ftype}; path = {q(n)}; sourceTree = "<group>"; }};')
w('/* End PBXFileReference section */')
w('')

w('/* Begin PBXFileSystemSynchronizedRootGroup section */')
w(f'\t\t{ids["synced"]} /* Simpl */ = {{')
w('\t\t\tisa = PBXFileSystemSynchronizedRootGroup;')
w('\t\t\tpath = Simpl;')
w('\t\t\tsourceTree = "<group>";')
w('\t\t};')
w('/* End PBXFileSystemSynchronizedRootGroup section */')
w('')

w('/* Begin PBXFrameworksBuildPhase section */')
w(f'\t\t{ids["frameworks"]} /* Frameworks */ = {{')
w('\t\t\tisa = PBXFrameworksBuildPhase;')
w('\t\t\tbuildActionMask = 2147483647;')
w('\t\t\tfiles = (')
w('\t\t\t);')
w('\t\t\trunOnlyForDeploymentPostprocessing = 0;')
w('\t\t};')
w('/* End PBXFrameworksBuildPhase section */')
w('')

w('/* Begin PBXGroup section */')
w(f'\t\t{ids["main"]} = {{')
w('\t\t\tisa = PBXGroup;')
w('\t\t\tchildren = (')
w(f'\t\t\t\t{ids["synced"]} /* Simpl */,')
w(f'\t\t\t\t{ids["shared"]} /* Shared */,')
w(f'\t\t\t\t{ids["support"]} /* Support */,')
w(f'\t\t\t\t{ids["repo"]} /* BetterCourseViewer */,')
w(f'\t\t\t\t{ids["products"]} /* Products */,')
w('\t\t\t);')
w('\t\t\tsourceTree = "<group>";')
w('\t\t};')
w(f'\t\t{ids["products"]} /* Products */ = {{')
w('\t\t\tisa = PBXGroup;')
w('\t\t\tchildren = (')
w(f'\t\t\t\t{ids["app"]} /* {NAME}.app */,')
w('\t\t\t);')
w('\t\t\tname = Products;')
w('\t\t\tsourceTree = "<group>";')
w('\t\t};')
w(f'\t\t{ids["support"]} /* Support */ = {{')
w('\t\t\tisa = PBXGroup;')
w('\t\t\tchildren = (')
w(f'\t\t\t\t{ids["infoplist"]} /* Info.plist */,')
w('\t\t\t);')
w('\t\t\tpath = Support;')
w('\t\t\tsourceTree = "<group>";')
w('\t\t};')
w(f'\t\t{ids["repo"]} /* BetterCourseViewer */ = {{')
w('\t\t\tisa = PBXGroup;')
w('\t\t\tchildren = (')
w(f'\t\t\t\t{ids["extension"]} /* extension */,')
w('\t\t\t);')
w('\t\t\tname = BetterCourseViewer;')
w('\t\t\tpath = ..;')
w('\t\t\tsourceTree = "<group>";')
w('\t\t};')
groups = sorted(set(g for g, *_ in shared_files))
w(f'\t\t{ids["shared"]} /* Shared */ = {{')
w('\t\t\tisa = PBXGroup;')
w('\t\t\tchildren = (')
for g in groups:
    w(f'\t\t\t\t{oid("group", "shared", g)} /* {g} */,')
w('\t\t\t);')
w('\t\t\tname = Shared;')
w('\t\t\tpath = ../ios/SimplCourses;')
w('\t\t\tsourceTree = SOURCE_ROOT;')
w('\t\t};')
for g in groups:
    w(f'\t\t{oid("group", "shared", g)} /* {g} */ = {{')
    w('\t\t\tisa = PBXGroup;')
    w('\t\t\tchildren = (')
    for group, n, fid, bid, kind in shared_files:
        if group == g:
            w(f'\t\t\t\t{fid} /* {n} */,')
    w('\t\t\t);')
    w(f'\t\t\tpath = {g};')
    w('\t\t\tsourceTree = "<group>";')
    w('\t\t};')
w('/* End PBXGroup section */')
w('')

w('/* Begin PBXNativeTarget section */')
w(f'\t\t{ids["target"]} /* {NAME} */ = {{')
w('\t\t\tisa = PBXNativeTarget;')
w(f'\t\t\tbuildConfigurationList = {ids["target_configs"]} /* Build configuration list for PBXNativeTarget "{NAME}" */;')
w('\t\t\tbuildPhases = (')
w(f'\t\t\t\t{ids["sources"]} /* Sources */,')
w(f'\t\t\t\t{ids["frameworks"]} /* Frameworks */,')
w(f'\t\t\t\t{ids["resources"]} /* Resources */,')
w('\t\t\t);')
w('\t\t\tbuildRules = (')
w('\t\t\t);')
w('\t\t\tdependencies = (')
w('\t\t\t);')
w('\t\t\tfileSystemSynchronizedGroups = (')
w(f'\t\t\t\t{ids["synced"]} /* Simpl */,')
w('\t\t\t);')
w(f'\t\t\tname = {NAME};')
w('\t\t\tpackageProductDependencies = (')
w('\t\t\t);')
w(f'\t\t\tproductName = {NAME};')
w(f'\t\t\tproductReference = {ids["app"]} /* {NAME}.app */;')
w('\t\t\tproductType = "com.apple.product-type.application";')
w('\t\t};')
w('/* End PBXNativeTarget section */')
w('')

w('/* Begin PBXProject section */')
w(f'\t\t{ids["project"]} /* Project object */ = {{')
w('\t\t\tisa = PBXProject;')
w('\t\t\tattributes = {')
w('\t\t\t\tBuildIndependentTargetsInParallel = YES;')
w('\t\t\t\tLastSwiftUpdateCheck = 2600;')
w('\t\t\t\tLastUpgradeCheck = 2600;')
w('\t\t\t\tTargetAttributes = {')
w(f'\t\t\t\t\t{ids["target"]} = {{')
w('\t\t\t\t\t\tCreatedOnToolsVersion = 26.0;')
w('\t\t\t\t\t};')
w('\t\t\t\t};')
w('\t\t\t};')
w(f'\t\t\tbuildConfigurationList = {ids["project_configs"]} /* Build configuration list for PBXProject "{NAME}" */;')
w('\t\t\tdevelopmentRegion = en;')
w('\t\t\thasScannedForEncodings = 0;')
w('\t\t\tknownRegions = (')
w('\t\t\t\ten,')
w('\t\t\t\tBase,')
w('\t\t\t);')
w(f'\t\t\tmainGroup = {ids["main"]};')
w('\t\t\tminimizedProjectReferenceProxies = 1;')
w('\t\t\tpreferredProjectObjectVersion = 77;')
w(f'\t\t\tproductRefGroup = {ids["products"]} /* Products */;')
w('\t\t\tprojectDirPath = "";')
w('\t\t\tprojectRoot = "";')
w('\t\t\ttargets = (')
w(f'\t\t\t\t{ids["target"]} /* {NAME} */,')
w('\t\t\t);')
w('\t\t};')
w('/* End PBXProject section */')
w('')

w('/* Begin PBXResourcesBuildPhase section */')
w(f'\t\t{ids["resources"]} /* Resources */ = {{')
w('\t\t\tisa = PBXResourcesBuildPhase;')
w('\t\t\tbuildActionMask = 2147483647;')
w('\t\t\tfiles = (')
for group, n, fid, bid, kind in shared_files:
    if kind != 'swift':
        w(f'\t\t\t\t{bid} /* {n} in Resources */,')
w(f'\t\t\t\t{ids["extension_build"]} /* extension in Resources */,')
w('\t\t\t);')
w('\t\t\trunOnlyForDeploymentPostprocessing = 0;')
w('\t\t};')
w('/* End PBXResourcesBuildPhase section */')
w('')

w('/* Begin PBXSourcesBuildPhase section */')
w(f'\t\t{ids["sources"]} /* Sources */ = {{')
w('\t\t\tisa = PBXSourcesBuildPhase;')
w('\t\t\tbuildActionMask = 2147483647;')
w('\t\t\tfiles = (')
for group, n, fid, bid, kind in shared_files:
    if kind == 'swift':
        w(f'\t\t\t\t{bid} /* {n} in Sources */,')
w('\t\t\t);')
w('\t\t\trunOnlyForDeploymentPostprocessing = 0;')
w('\t\t};')
w('/* End PBXSourcesBuildPhase section */')
w('')

COMMON_PROJECT = {
    'ALWAYS_SEARCH_USER_PATHS': 'NO',
    'ASSETCATALOG_COMPILER_GENERATE_SWIFT_ASSET_SYMBOL_EXTENSIONS': 'YES',
    'CLANG_ANALYZER_NONNULL': 'YES',
    'CLANG_CXX_LANGUAGE_STANDARD': '"gnu++20"',
    'CLANG_ENABLE_MODULES': 'YES',
    'CLANG_ENABLE_OBJC_ARC': 'YES',
    'CLANG_ENABLE_OBJC_WEAK': 'YES',
    'CLANG_WARN_BOOL_CONVERSION': 'YES',
    'CLANG_WARN_CONSTANT_CONVERSION': 'YES',
    'CLANG_WARN_DEPRECATED_OBJC_IMPLEMENTATIONS': 'YES',
    'CLANG_WARN_EMPTY_BODY': 'YES',
    'CLANG_WARN_ENUM_CONVERSION': 'YES',
    'CLANG_WARN_INFINITE_RECURSION': 'YES',
    'CLANG_WARN_INT_CONVERSION': 'YES',
    'CLANG_WARN_UNGUARDED_AVAILABILITY': 'YES_AGGRESSIVE',
    'CLANG_WARN_UNREACHABLE_CODE': 'YES',
    'COPY_PHASE_STRIP': 'NO',
    'ENABLE_STRICT_OBJC_MSGSEND': 'YES',
    'ENABLE_USER_SCRIPT_SANDBOXING': 'YES',
    'GCC_C_LANGUAGE_STANDARD': 'gnu17',
    'GCC_NO_COMMON_BLOCKS': 'YES',
    'GCC_WARN_64_TO_32_BIT_CONVERSION': 'YES',
    'GCC_WARN_ABOUT_RETURN_TYPE': 'YES_ERROR',
    'GCC_WARN_UNDECLARED_SELECTOR': 'YES',
    'GCC_WARN_UNINITIALIZED_AUTOS': 'YES_AGGRESSIVE',
    'GCC_WARN_UNUSED_FUNCTION': 'YES',
    'GCC_WARN_UNUSED_VARIABLE': 'YES',
    'LOCALIZATION_PREFERS_STRING_CATALOGS': 'YES',
    'MACOSX_DEPLOYMENT_TARGET': DEPLOYMENT,
    'MARKETING_VERSION': MARKETING_VERSION,
    'CURRENT_PROJECT_VERSION': BUILD,
    'SDKROOT': 'macosx',
    'SWIFT_VERSION': '5.9',
}
DEBUG_PROJECT = dict(COMMON_PROJECT, **{
    'DEBUG_INFORMATION_FORMAT': 'dwarf',
    'ENABLE_TESTABILITY': 'YES',
    'GCC_DYNAMIC_NO_PIC': 'NO',
    'GCC_OPTIMIZATION_LEVEL': '0',
    'GCC_PREPROCESSOR_DEFINITIONS': '(\n\t\t\t\t\t"DEBUG=1",\n\t\t\t\t\t"$(inherited)",\n\t\t\t\t)',
    'ONLY_ACTIVE_ARCH': 'YES',
    'SWIFT_ACTIVE_COMPILATION_CONDITIONS': '"DEBUG $(inherited)"',
    'SWIFT_OPTIMIZATION_LEVEL': '"-Onone"',
})
RELEASE_PROJECT = dict(COMMON_PROJECT, **{
    'DEBUG_INFORMATION_FORMAT': '"dwarf-with-dsym"',
    'ENABLE_NS_ASSERTIONS': 'NO',
    'SWIFT_COMPILATION_MODE': 'wholemodule',
})
TARGET = {
    'ASSETCATALOG_COMPILER_APPICON_NAME': 'AppIcon',
    'ASSETCATALOG_COMPILER_GLOBAL_ACCENT_COLOR_NAME': 'AccentColor',
    'CODE_SIGN_IDENTITY': '"-"',
    'CODE_SIGN_STYLE': 'Automatic',
    'COMBINE_HIDPI_IMAGES': 'YES',
    'DEVELOPMENT_TEAM': '""',
    'ENABLE_HARDENED_RUNTIME': 'YES', # (what notarization needs; the web view runs out of process, so nothing more is asked)
    'ENABLE_PREVIEWS': 'YES',
    'GENERATE_INFOPLIST_FILE': 'NO',
    'INFOPLIST_FILE': 'Support/Info.plist',
    'LD_RUNPATH_SEARCH_PATHS': '(\n\t\t\t\t\t"$(inherited)",\n\t\t\t\t\t"@executable_path/../Frameworks",\n\t\t\t\t)',
    'PRODUCT_BUNDLE_IDENTIFIER': BUNDLE_ID,
    'PRODUCT_NAME': '"$(TARGET_NAME)"',
    'SWIFT_EMIT_LOC_STRINGS': 'NO',
}


def config(cid, name, settings):
    w(f'\t\t{cid} /* {name} */ = {{')
    w('\t\t\tisa = XCBuildConfiguration;')
    w('\t\t\tbuildSettings = {')
    for k in sorted(settings):
        w(f'\t\t\t\t{k} = {settings[k]};')
    w('\t\t\t};')
    w(f'\t\t\tname = {name};')
    w('\t\t};')


w('/* Begin XCBuildConfiguration section */')
config(ids['project_debug'], 'Debug', DEBUG_PROJECT)
config(ids['project_release'], 'Release', RELEASE_PROJECT)
config(ids['target_debug'], 'Debug', TARGET)
config(ids['target_release'], 'Release', TARGET)
w('/* End XCBuildConfiguration section */')
w('')

w('/* Begin XCConfigurationList section */')
for lid, label, dbg, rel in [
    (ids['project_configs'], f'PBXProject "{NAME}"', ids['project_debug'], ids['project_release']),
    (ids['target_configs'], f'PBXNativeTarget "{NAME}"', ids['target_debug'], ids['target_release']),
]:
    w(f'\t\t{lid} /* Build configuration list for {label} */ = {{')
    w('\t\t\tisa = XCConfigurationList;')
    w('\t\t\tbuildConfigurations = (')
    w(f'\t\t\t\t{dbg} /* Debug */,')
    w(f'\t\t\t\t{rel} /* Release */,')
    w('\t\t\t);')
    w('\t\t\tdefaultConfigurationIsVisible = 0;')
    w('\t\t\tdefaultConfigurationName = Release;')
    w('\t\t};')
w('/* End XCConfigurationList section */')
w('\t};')
w(f'\trootObject = {ids["project"]} /* Project object */;')
w('}')

os.makedirs(os.path.join(PROJ, 'project.xcworkspace'), exist_ok=True)
os.makedirs(os.path.join(PROJ, 'xcshareddata', 'xcschemes'), exist_ok=True)
with open(os.path.join(PROJ, 'project.pbxproj'), 'w') as f:
    f.write('\n'.join(lines) + '\n')
with open(os.path.join(PROJ, 'project.xcworkspace', 'contents.xcworkspacedata'), 'w') as f:
    f.write('<?xml version="1.0" encoding="UTF-8"?>\n<Workspace\n   version = "1.0">\n   <FileRef\n      location = "self:">\n   </FileRef>\n</Workspace>\n')

ref = f'''            <BuildableReference
               BuildableIdentifier = "primary"
               BlueprintIdentifier = "{ids["target"]}"
               BuildableName = "{NAME}.app"
               BlueprintName = "{NAME}"
               ReferencedContainer = "container:{NAME}.xcodeproj">
            </BuildableReference>'''
scheme = f'''<?xml version="1.0" encoding="UTF-8"?>
<Scheme
   LastUpgradeVersion = "2600"
   version = "1.7">
   <BuildAction
      parallelizeBuildables = "YES"
      buildImplicitDependencies = "YES">
      <BuildActionEntries>
         <BuildActionEntry
            buildForTesting = "YES"
            buildForRunning = "YES"
            buildForProfiling = "YES"
            buildForArchiving = "YES"
            buildForAnalyzing = "YES">
{ref}
         </BuildActionEntry>
      </BuildActionEntries>
   </BuildAction>
   <TestAction
      buildConfiguration = "Debug"
      selectedDebuggerIdentifier = "Xcode.DebuggerFoundation.Debugger.LLDB"
      selectedLauncherIdentifier = "Xcode.DebuggerFoundation.Launcher.LLDB"
      shouldUseLaunchSchemeArgsEnv = "YES">
      <Testables>
      </Testables>
   </TestAction>
   <LaunchAction
      buildConfiguration = "Debug"
      selectedDebuggerIdentifier = "Xcode.DebuggerFoundation.Debugger.LLDB"
      selectedLauncherIdentifier = "Xcode.DebuggerFoundation.Launcher.LLDB"
      launchStyle = "0"
      useCustomWorkingDirectory = "NO"
      ignoresPersistentStateOnLaunch = "NO"
      debugDocumentVersioning = "YES"
      debugServiceExtension = "internal"
      allowLocationSimulation = "YES">
      <BuildableProductRunnable
         runnableDebuggingMode = "0">
{ref.replace("            ", "         ", 1)}
      </BuildableProductRunnable>
   </LaunchAction>
   <ProfileAction
      buildConfiguration = "Release"
      shouldUseLaunchSchemeArgsEnv = "YES"
      savedToolIdentifier = ""
      useCustomWorkingDirectory = "NO"
      debugDocumentVersioning = "YES">
      <BuildableProductRunnable
         runnableDebuggingMode = "0">
{ref}
      </BuildableProductRunnable>
   </ProfileAction>
   <AnalyzeAction
      buildConfiguration = "Debug">
   </AnalyzeAction>
   <ArchiveAction
      buildConfiguration = "Release"
      revealArchiveInOrganizer = "YES">
   </ArchiveAction>
</Scheme>
'''
with open(os.path.join(PROJ, 'xcshareddata', 'xcschemes', f'{NAME}.xcscheme'), 'w') as f:
    f.write(scheme)
print(f'wrote {os.path.relpath(PROJ, ROOT)} ({len(shared_files)} shared files)')
