from datetime import datetime
import os
import json
import yaml
import argparse
import re

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
    return list(set(matches))

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
    return match.group(1)

parser = argparse.ArgumentParser()
parser.add_argument("--repo", required=True)
parser.add_argument("--version", required=True)
parser.add_argument("--tag", required=True)
parser.add_argument("--prerelease", default=False)
opts = parser.parse_args()

project_file = "./Shokofin/Shokofin.csproj"
version = opts.version
tag = opts.tag
prerelease = bool(opts.prerelease)
short_version = ".".join(version.split(".")[:3])
build_number = int(version.split(".")[-1])

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

# Load the manifest.json file into memory.
with open(jellyfin_repo_file, "r") as file:
    repos = json.load(file)
    repo = repos[0]

versions = []

# For every found framework, generate a zip file for the target framework and ABI.
try:
    for framework in extract_target_framework(project_file):
        target_abi = extract_target_abi(project_file, framework)
        target_abi_high = ".".join(target_abi.split(".")[:-1])
        target_abi_low = target_abi.split(".")[1]
        artifacts = extract_packages_to_output(project_file, framework)

        if build_number != "0":
            generated_version = f"{short_version}.{build_number}{target_abi_low}"
        else:
            generated_version = f"{short_version}.{target_abi_low}"
        generated_changelog = f"Only compatible with **{target_abi_high}.z**.\n\nSee the [release notes](https://github.com/ShokoAnime/Shokofin/releases/tag/{tag}) for more info."
        if changelog:
            generated_changelog += f"\n\n---\n\n{changelog}"

        data = yaml.safe_load(build_file_contents)
        data["changelog"] = generated_changelog
        data["artifacts"] = list(set(data["artifacts"] + artifacts))
        data["targetAbi"] = target_abi + ".0"
        with open(build_file, "w") as file:
            yaml.dump(data, file, sort_keys=False)

        zipfile=os.popen("jprm --verbosity=debug plugin build \".\" --output=\"%s\" --version=\"%s\" --dotnet-framework=\"%s\"" % (artifact_dir, generated_version, framework)).read().strip()

        # read the checksum file jprm wrote
        checksum = open(zipfile + ".md5sum", "r").read().strip()[:32]
        timestamp = os.path.getmtime(zipfile)
        new_zipfile = os.path.join(artifact_dir, f"shoko_{version}_for_{target_abi_high}.zip")
        os.rename(zipfile, new_zipfile)
        os.remove(zipfile + ".md5sum")
        os.remove(zipfile + ".meta.json")

        jellyfin_plugin_release_url=f"{jellyfin_repo_url}/{tag}/shoko_{version}_for_{target_abi_high}.zip"
        os.system("jprm repo add --plugin-url=%s %s %s" % (jellyfin_plugin_release_url, jellyfin_repo_file, new_zipfile))

        versions.append({
            "version": generated_version,
            "changelog": generated_changelog,
            "targetAbi": target_abi + ".0",
            "sourceUrl": jellyfin_plugin_release_url,
            "checksum": checksum,
            "timestamp": datetime.fromtimestamp(timestamp).strftime("%Y-%m-%dT%H:%M:%SZ"),
        })
finally:
    # Restore the original build.yaml after we're done
    with open(build_file, "w") as file:
        file.write(build_file_contents)

# Update the repository file with the newest data from the build.yaml
if "name" in data:
    repo["name"] = data["name"]
if "owner" in data:
    repo["owner"] = data["owner"]
if "overview" in data:
    repo["overview"] = data["overview"]
if "description" in data:
    repo["description"] = data["description"]
if "category" in data:
    repo["category"] = data["category"]
if "imageUrl" in data:
    repo["imageUrl"] = data["imageUrl"]
for version_data in reversed(versions):
    repo["versions"].insert(0, version_data)

# Compact the unstable manifest after building, so it only contains the last 10 versions.
if prerelease:
    if "versions" in repo and len(repo["versions"]) > 10:
        repo["versions"] = repo["versions"][:10]

# Update the repository file
with open(jellyfin_repo_file, "w") as file:
    json.dump(repos, file, indent=4)

print(version)
