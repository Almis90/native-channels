#!/usr/bin/env python3
"""Build real Swift frameworks and combine their platform slices into XCFrameworks."""
import argparse, pathlib, plistlib, shutil, subprocess

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "artifacts/native"
OUT.mkdir(parents=True, exist_ok=True)

def run(*args):
    subprocess.run([str(a) for a in args], check=True)

def framework(name, source, sdk, arch, suffix, dependencies=()):
    macos = sdk == "macosx" and not suffix
    target = f"{arch}-apple-macosx14.0" if macos else f"{arch}-apple-ios15.0{suffix}"
    location = OUT / f"{sdk}{suffix}-{arch}" / f"{name}.framework"
    location.mkdir(parents=True, exist_ok=True)
    (location / "Headers").mkdir(exist_ok=True)
    (location / "Modules").mkdir(exist_ok=True)
    module = location / "Modules" / f"{name}.swiftmodule"
    if module.is_file(): module.unlink()
    module.mkdir(exist_ok=True)
    triple = f"{arch}-apple-macos" if macos else f"{arch}-apple-ios{suffix}"
    sdkpath = subprocess.check_output(["xcrun", "--sdk", sdk, "--show-sdk-path"], text=True).strip()
    cmd = ["xcrun", "--sdk", sdk, "swiftc", "-swift-version", "5", "-parse-as-library",
           "-emit-library", "-emit-module", "-enable-library-evolution", "-module-name", name,
           "-target", target, "-sdk", sdkpath, "-emit-module-path", module / f"{triple}.swiftmodule",
           "-emit-module-interface-path", module / f"{triple}.swiftinterface",
           "-emit-objc-header-path", location / "Headers" / f"{name}-Swift.h",
           "-Xlinker", "-install_name", "-Xlinker", f"@rpath/{name}.framework/{name}",
           "-o", location / name, *([source] if source.is_file() else sorted(source.glob("*.swift")))]
    if suffix == "-macabi":
        support = pathlib.Path(sdkpath) / "System/iOSSupport"
        cmd.extend(["-F", support / "System/Library/Frameworks",
                    "-I", support / "usr/include", "-L", support / "usr/lib"])
    for dependency in dependencies:
        cmd.extend(["-F", dependency.parent, "-framework", dependency.stem])
    run(*cmd)
    (location / "Modules/module.modulemap").write_text(
        f'framework module {name} {{ header "{name}-Swift.h" export * }}\n')
    with (location / "Info.plist").open("wb") as f:
        plistlib.dump({"CFBundleIdentifier": f"dev.nativechannels.{name}", "CFBundleName": name,
                      "CFBundleExecutable": name, "CFBundlePackageType": "FMWK",
                      "CFBundleVersion": "1", "CFBundleShortVersionString": "0.3.0",
                      "MinimumOSVersion": "14.0" if macos else "15.0", "CFBundleSupportedPlatforms":
                      ["MacOSX" if sdk == "macosx" else "iPhoneSimulator" if suffix else "iPhoneOS"]}, f)
    return location

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--macos-only", action="store_true", help="Build native macOS arm64/x64 development slices")
    args = parser.parse_args()
    slices = {"NativeChannelsNative": [], "NativeChannelsSample": []}
    platforms = [("macosx", "", ["arm64", "x86_64"])] if args.macos_only else [("iphoneos", "", ["arm64"]),
                                 ("iphonesimulator", "-simulator", ["arm64", "x86_64"]),
                                 ("macosx", "-macabi", ["arm64", "x86_64"])]
    for sdk, suffix, arches in platforms:
        built = {name: [] for name in slices}
        for arch in arches:
            core = framework("NativeChannelsNative", ROOT / "native/apple/Sources", sdk, arch, suffix)
            probe = framework("NativeChannelsSample", ROOT / "native/apple/Samples", sdk, arch, suffix, [core])
            built["NativeChannelsNative"].append(core)
            built["NativeChannelsSample"].append(probe)
        for name, frameworks in built.items():
            dest = OUT / (sdk + suffix) / f"{name}.framework"
            if dest.exists(): shutil.rmtree(dest)
            shutil.copytree(frameworks[0], dest)
            if len(frameworks) > 1:
                run("xcrun", "lipo", "-create", *[f / name for f in frameworks], "-output", dest / name)
                for extra in frameworks[1:]:
                    shutil.copytree(extra / "Modules" / f"{name}.swiftmodule", dest / "Modules" / f"{name}.swiftmodule", dirs_exist_ok=True)
            slices[name].append(dest)

    for name, frameworks in slices.items():
        dest = OUT / f"{name}.xcframework"
        if dest.exists(): shutil.rmtree(dest)
        args = ["xcodebuild", "-create-xcframework"]
        for framework_path in frameworks: args.extend(["-framework", framework_path])
        run(*args, "-output", dest)


if __name__ == "__main__":
    main()
