import os
import json
import yaml
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--version', required=True)
parser.add_argument('--prerelease')
opts = parser.parse_args()

version = opts.version
prerelease = bool(opts.prerelease)

artifact_dir = os.path.join(os.getcwd(), 'artifacts')
os.mkdir(artifact_dir)

if prerelease:
  jellyfin_repo_file="./manifest-unstable.json"
else:
  jellyfin_repo_file="./manifest.json"

jellyfin_repo_url="https://github.com/ShokoAnime/Shokofin/releases/download"

# Add changelog to the build yaml before we generate the release.
build_file = './build.yaml'

with open(build_file, 'r') as file:
    data = yaml.safe_load(file)

if "changelog" in data:
    data["changelog"] = os.environ["CHANGELOG"].strip()

with open(build_file, 'w') as file:
    yaml.dump(data, file, sort_keys=False)

zipfile=os.popen('jprm --verbosity=debug plugin build "." --output="%s" --version="%s" --dotnet-framework="net6.0"' % (artifact_dir, version)).read().strip()

jellyfin_plugin_release_url=f'{jellyfin_repo_url}/{version}/shoko_{version}.zip'

os.system('jprm repo add --plugin-url=%s %s %s' % (jellyfin_plugin_release_url, jellyfin_repo_file, zipfile))

# Compact the unstable manifest after building, so it only contains the last 5 versions.
if prerelease:
  with open(jellyfin_repo_file, 'r') as file:
      data = json.load(file)

  for item in data:
      if 'versions' in item and len(item['versions']) > 5:
          item['versions'] = item['versions'][:5]

  with open(jellyfin_repo_file, 'w') as file:
      json.dump(data, file, indent=4)

print(version)
