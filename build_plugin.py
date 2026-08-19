from datetime import datetime
import os
import json
import yaml
import argparse
import re
import subprocess

def extract_target_framework(csproj_path):
    with open(csproj_path, "r") as file:
        content = file.read()
    target_framework_match = re.compile(r"<TargetFramework>(.*?)<\/TargetFramework>", re.IGNORECASE).search(content)
    target_frameworks_match = re.compile(r"<TargetFrameworks>(.*?)<\/TargetFrameworks>", re.IGNORECASE).search(content)
    if target_framework_match:
        return [target_framework_match.group(1)]
    elif target_frameworks_match:
        return target_frameworks_match.group(1).split(";")
    else:
        return None

def extract_packages_to_output(csproj_path, framework):
    with open(csproj_path, "r") as file:
        content = file.read()
    pattern = re.compile(
        rf'<CommonPackageReference\s+Include="([^"]+)"\s+'
        rf'Version="[^"]+"\s+'
        rf'TargetFramework="{re.escape(framework)}"\s*/>'
    )
    matches = [match.group(1) + ".dll" for match in pattern.finditer(content)]
    return list(dict.fromkeys(matches))

def extract_target_abi(csproj_path, framework):
    with open(csproj_path, "r") as file:
        content = file.read()
    pattern = re.compile(
        rf'<PackageReference\s+Include="Jellyfin\.Controller"\s+'
        rf'Version="([^"]+)"\s+'
        rf'TargetFramework="{re.escape(framework)}"\s*/>',
        re.IGNORECASE,
    )
    match = pattern.search(content)
    if not match:
        raise Exception(
            f"Jellyfin.Controller not found for framework '{framework}' in {os.path.basename(csproj_path)}"
        )
    return match.group(1).split("-")[0]

parser = argparse.ArgumentParser()
parser.add_argument("--repo", required=True)
parser.add_argument("--version", required=True)
parser.add_argument("--tag", required=True)
parser.add_argument("--prerelease", action="store_true")
opts = parser.parse_args()

project_file = "./Shokofin/Shokofin.csproj"
version = opts.version
tag = opts.tag
prerelease = opts.prerelease
version_parts = version.split(".")
short_version = ".".join(version_parts[:3])
build_number = int(version_parts[3]) if len(version_parts) > 3 else 0

artifact_dir = os.path.join(os.getcwd(), "artifacts")
if not os.path.exists(artifact_dir):
    os.mkdir(artifact_dir)

jellyfin_repo_file="./manifest.json"
jellyfin_repo_url=f"https://github.com/{opts.repo}/releases/download"

# Load the build.yaml file into memory.
build_file = "./build.yaml"
with open(build_file, "r") as file:
    build_file_contents = file.read()
    data = yaml.safe_load(build_file_contents)

# Add changelog to the build yaml before we generate the release.
if "changelog" in data:
    if "CHANGELOG" in os.environ:
        data["changelog"] = os.environ["CHANGELOG"].strip()
    else:
        data["changelog"] = ""
changelog = data["changelog"]

# For every found framework, generate a zip file for the target framework and ABI.
try:
    for framework in extract_target_framework(project_file):
        target_abi = extract_target_abi(project_file, framework)
        target_abi_parts = target_abi.split(".")
        if target_abi_parts[0] == "10":
            target_abi_high = ".".join(target_abi_parts[:2]) + ".z"
            target_abi_low = target_abi_parts[1]
        else:
            target_abi_high = target_abi_parts[0] + ".y.z"
            target_abi_low = target_abi_parts[0]
        artifacts = extract_packages_to_output(project_file, framework)

        if build_number != 0:
            generated_version = f"{short_version}.{build_number}{target_abi_low}"
        else:
            generated_version = f"{short_version}.{target_abi_low}"
        generated_changelog = f"Only compatible with **{target_abi_high}**.\n\nSee the [release notes](https://github.com/ShokoAnime/Shokofin/releases/tag/{tag}) for more info."
        if changelog:
            generated_changelog += f"\n\n---\n\n{changelog}"

        data = yaml.safe_load(build_file_contents)
        data["changelog"] = generated_changelog
        data["artifacts"] = list(dict.fromkeys(data["artifacts"] + artifacts))
        data["targetAbi"] = target_abi + ".0"
        with open(build_file, "w") as file:
            yaml.dump(data, file, sort_keys=False)

        result = subprocess.run(
            [
                "jprm",
                "--verbosity=debug",
                "plugin",
                "build",
                ".",
                f"--output={artifact_dir}",
                f"--version={generated_version}",
                f"--dotnet-framework={framework}",
            ],
            check=True,
            stdout=subprocess.PIPE,
            text=True,
        )
        zipfile = result.stdout.strip()
        if not zipfile:
            raise RuntimeError(f"JPRM did not return a package path for framework '{framework}'")

        # read the checksum file jprm wrote
        checksum = open(zipfile + ".md5sum", "r").read().strip()[:32]
        timestamp = os.path.getmtime(zipfile)
        new_zipfile = os.path.join(artifact_dir, f"shoko_{version}_for_{target_abi_high}.zip")
        os.rename(zipfile, new_zipfile)
        os.remove(zipfile + ".md5sum")
        os.remove(zipfile + ".meta.json")

        jellyfin_plugin_release_url=f"{jellyfin_repo_url}/{tag}/shoko_{version}_for_{target_abi_high}.zip"
        subprocess.run(
            [
                "jprm",
                "repo",
                "add",
                f"--plugin-url={jellyfin_plugin_release_url}",
                jellyfin_repo_file,
                new_zipfile,
            ],
            check=True,
        )
finally:
    # Restore the original build.yaml after we're done
    with open(build_file, "w") as file:
        file.write(build_file_contents)

# Compact the unstable manifest after building, so it only contains the last 10 versions.
if prerelease:
    with open(jellyfin_repo_file, "r") as file:
        repos = json.load(file)
        repo = repos[0]
    if "versions" in repo and len(repo["versions"]) > 10:
        repo["versions"] = repo["versions"][:10]

    # Update the repository file
    with open(jellyfin_repo_file, "w") as file:
        json.dump(repos, file, indent=4)

print(version)
