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
        return target_framework_match.group(1)
    elif target_frameworks_match:
        return target_frameworks_match.group(1).split(";")[0]  # Return the first framework
    else:
        return None

parser = argparse.ArgumentParser()
parser.add_argument("--repo", required=True)
parser.add_argument("--version", required=True)
parser.add_argument("--tag", required=True)
parser.add_argument("--prerelease", default=False)
opts = parser.parse_args()

framework = extract_target_framework("./Shokofin/Shokofin.csproj")
version = opts.version
tag = opts.tag
prerelease = bool(opts.prerelease)

artifact_dir = os.path.join(os.getcwd(), "artifacts")
if not os.path.exists(artifact_dir):
    os.mkdir(artifact_dir)

jellyfin_repo_file="./manifest.json"
jellyfin_repo_url=f"https://github.com/{opts.repo}/releases/download"

# Add changelog to the build yaml before we generate the release.
build_file = "./build.yaml"

with open(build_file, "r") as file:
    data = yaml.safe_load(file)

if "changelog" in data:
    if "CHANGELOG" in os.environ:
        data["changelog"] = os.environ["CHANGELOG"].strip()
    else:
        data["changelog"] = ""

with open(build_file, "w") as file:
    yaml.dump(data, file, sort_keys=False)

zipfile=os.popen("jprm --verbosity=debug plugin build \".\" --output=\"%s\" --version=\"%s\" --dotnet-framework=\"%s\"" % (artifact_dir, version, framework)).read().strip()

jellyfin_plugin_release_url=f"{jellyfin_repo_url}/{tag}/shoko_{version}.zip"

os.system("jprm repo add --plugin-url=%s %s %s" % (jellyfin_plugin_release_url, jellyfin_repo_file, zipfile))

# Compact the unstable manifest after building, so it only contains the last 5 versions.
if prerelease:
    with open(jellyfin_repo_file, "r") as file:
        data = json.load(file)

    for item in data:
        if "versions" in item and len(item["versions"]) > 5:
            item["versions"] = item["versions"][:5]

    with open(jellyfin_repo_file, "w") as file:
        json.dump(data, file, indent=4)

print(version)
