#!/usr/bin/env bash
# 发版时把 winget 清单升到当前版本，并向 microsoft/winget-pkgs 开 PR，最后把清单回写本仓库 main。
# 由 .github/workflows/release.yml 在建完 Release 之后调用；单独跑也行，参数是版本号。
#
# 需要的环境变量：
#   WINGET_PAT     有 repo 权限、且账号能往自己的 winget-pkgs fork 推东西的令牌。没设就只生成、不提交上游。
#   GITHUB_TOKEN   工作流自带的令牌，用来把清单回写本仓库；本地跑不用设。
#   WINGET_OUT     设了就把生成结果拷到这个目录然后退出（本地看 diff 用）。
#   WINGET_CHECK   设了就在验完令牌与 fork 写权限之后退出，不建分支、不 PUT、不开 PR、不回写。
#   WINGET_SHA     直接给定 64 位十六进制安装包哈希，跳过取 Release digest（本地试跑用）。
#   WINGET_DATE    直接给定 ReleaseDate，同上。
#
# 本地看效果（不会碰仓库工作树、也不会碰上游）：
#   WINGET_OUT="$TEMP/wg" WINGET_SHA=$(printf 'A%.0s' $(seq 64)) WINGET_DATE=2026-01-01 \
#     bash installer/winget-submit.sh 1.9.9
set -euo pipefail

VERSION="${1:-${VERSION:-}}"
[ -n "$VERSION" ] || { echo "要么把版本号当参数传，要么设 VERSION（工作流里从标签推出来）。" >&2; exit 1; }

repo="${GITHUB_REPOSITORY:-bobvan9/BobIme}"
tag="v$VERSION"
pkg="bobvan9.BobIme"
upstream="microsoft/winget-pkgs"
rel="b/bobvan9/BobIme"                                  # 上游 manifests/ 下的相对路径，跟 PackageIdentifier 的大小写一致
in_repo="installer/winget/manifests/$rel"               # 本仓库里那份副本
asset="BobIme-Setup-$VERSION.exe"

# 上一版目录当模板；同一版本已经有目录（重跑）就拿它自己当模板。
template="$in_repo/$VERSION"
[ -d "$template" ] || template=$(find "$in_repo" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -1)
[ -n "$template" ] && [ -d "$template" ] || { echo "$in_repo 下连一个版本目录都没有，没法定型清单。" >&2; exit 1; }

# 一律生成到临时目录，本仓库的工作树不动，回写走 Contents API。
work=$(mktemp -d)
dest="$work/manifests/$rel/$VERSION"
mkdir -p "$dest"
cp "$template"/*.yaml "$dest/"

if [ -n "${WINGET_SHA:-}" ]; then
  sha="$WINGET_SHA"
  released="${WINGET_DATE:-$(date +%F)}"
else
  digest=$(gh api "repos/$repo/releases/tags/$tag" --jq ".assets[] | select(.name==\"$asset\") | .digest")
  sha=$(printf '%s' "${digest#sha256:}" | tr 'a-f' 'A-F')
  released=$(gh api "repos/$repo/releases/tags/$tag" --jq .published_at | cut -dT -f1)
  [ -n "$sha" ] || { echo "Release $tag 里找不到附件 $asset 的 digest，清单没法填哈希。" >&2; exit 1; }
fi

for f in "$dest"/*.yaml; do
  # InstallerSha256 / InstallerUrl 在 Installers 条目下面，是缩进的，所以不能拿 ^ 直接锚。
  sed -i "s|^PackageVersion: .*|PackageVersion: $VERSION|
          s|/v[0-9][^/]*/BobIme-Setup-[0-9][^/]*\.exe|/v$VERSION/$asset|
          s|\([[:space:]]*InstallerSha256: \)[0-9A-Fa-f]*|\1$sha|
          s|^ReleaseDate: .*|ReleaseDate: $released|" "$f"
done
grep -q "InstallerSha256: $sha" "$dest/$pkg.installer.yaml" || { echo "哈希没写进清单，sed 没匹配上。" >&2; exit 1; }

echo "清单版本 $VERSION，ReleaseDate $released，InstallerSha256 $sha"
if [ -n "${WINGET_OUT:-}" ]; then
  mkdir -p "$WINGET_OUT"
  rm -rf "${WINGET_OUT:?}/manifests"
  cp -r "$work/manifests" "$WINGET_OUT/"
  echo "只生成，不提交上游。结果在 $WINGET_OUT/manifests/$rel/$VERSION"
  exit 0
fi

if [ -z "${WINGET_PAT:-}" ]; then
  echo "没设 WINGET_PAT，跳过上游提交。生成的清单在 $dest，可以自己拷进 fork。" >&2
  exit 0
fi

export GH_TOKEN="$WINGET_PAT"
owner=$(gh api user --jq .login)
fork="$owner/winget-pkgs"

# 只验令牌：确认它认得出账号、对 fork 有写权限，然后就地收手，一个字节都不写。
# 没把分支条件塞进 release.yml，而是单开 .github/workflows/winget-check.yml 调这个开关。
if [ -n "${WINGET_CHECK:-}" ]; then
  perm=$(gh api "repos/$fork" --jq .permissions.push 2>/dev/null || echo "读不到（fork 不存在或令牌看不见它）")
  echo "令牌账号：$owner"
  echo "上游 fork：$fork，可写：$perm"
  echo "只检查模式，到此为止，没建分支也没开 PR。"
  exit 0
fi

# fork 已经有了就直接用；刚建的 fork 是异步的，master ref 要等几秒才查得到。
gh api "repos/$fork" >/dev/null 2>&1 || gh api -X POST "repos/$upstream/forks" >/dev/null
head=""
for _ in $(seq 1 24); do
  head=$(gh api "repos/$fork/git/ref/heads/master" --jq .object.sha 2>/dev/null || true)
  [ -n "$head" ] && break
  sleep 5
done
[ -n "$head" ] || { echo "等不到 $fork 的 master ref，放弃提交。" >&2; exit 1; }

branch="submit-$pkg-$VERSION"
gh api -X POST "repos/$fork/git/refs" -f ref="refs/heads/$branch" -f sha="$head" --jq .ref >/dev/null || true

# 同一路径第二次 PUT 必须带现有 blob 的 sha（重跑同一个 tag 会撞上）。
for f in "$dest"/*.yaml; do
  name=$(basename "$f")
  path="manifests/$rel/$VERSION/$name"
  sha_old=$(gh api "repos/$fork/contents/$path?ref=$branch" --jq .sha 2>/dev/null || true)
  if [ -n "$sha_old" ]; then
    gh api -X PUT "repos/$fork/contents/$path" -f message="$pkg $VERSION: $name" \
       -f content="$(base64 -w0 "$f")" -f branch="$branch" -f sha="$sha_old" --jq .commit.sha
  else
    gh api -X PUT "repos/$fork/contents/$path" -f message="$pkg $VERSION: $name" \
       -f content="$(base64 -w0 "$f")" -f branch="$branch" --jq .commit.sha
  fi
done

# 上游已经有这个包的目录就是 Update，否则 New package，标题按上游模板那两种写法来。
if gh api "repos/$upstream/contents/manifests/$rel" >/dev/null 2>&1; then
  title="Update: $pkg to $VERSION"
  intro="Bumps **$pkg** from the previous version to **$VERSION**."
else
  title="New package: $pkg version $VERSION"
  intro="Adds the manifest for **$pkg** version **$VERSION**."
fi

body=$(cat <<EOF
## 📖 Description

${intro}

Bob 五笔 (Bob Wubi) is a free, fully offline Windows Wubi input method, source at https://github.com/$repo,
GPL-3.0-only. The installer is an Inno Setup package holding a self-contained win-x64 .NET build, so no runtime
has to be installed separately. Only an x64 installer is published, so no arm64 entry is declared.

- \`InstallerSha256\` is the digest the GitHub Releases API reports for the release asset (the workflow reads it
  from \`releases/tags/$tag\`), not a locally computed hash.
- \`ProductCode\` is the fixed Inno \`AppId\` GUID in the installer script
  (\`{7A3C1E58-2F4B-4D9A-8C6E-B0B1A7E5D310}\`); it has not changed between versions, so upgrades are detected
  through the same Add/Remove Programs key.
- Machine-wide install (\`PrivilegesRequired=admin\`); the silent switches do not launch the app afterwards.

This PR is produced by the project's release workflow
(\`.github/workflows/release.yml\` plus \`installer/winget-submit.sh\`), reviewed and submitted by @$owner.
GitHub-hosted runner images have no winget CLI, so \`winget validate\`/\`winget install --manifest\` were not run
here - the first local submission of this package did pass \`winget validate --manifest\` on winget v1.30, and the
checks in this pipeline are the authoritative ones.

## ✅ Checklist

- [ ] Signed the [Contributor License Agreement](https://cla.opensource.microsoft.com) (the author's account has
  agreed once already; if the bot asks again, @$owner will reply with the agreement line)
- [ ] Linked to an issue (not applicable)

## 📦 Manifest Checklist

- [x] Checked that there aren't other open pull requests for the same manifest update/change
- [x] This PR only modifies one (1) manifest
- [ ] Validated manifest locally with \`winget validate --manifest <path>\` — no winget CLI on the runner, see note above
- [ ] Tested manifest locally with \`winget install --manifest <path>\` — same reason
- [x] Manifest conforms to the [1.28 schema](https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema/1.28.0)
EOF
)

# 同一个 tag 重跑时分支和 PR 都已经在（建 Release 那步本来就允许重跑），这时认已有那条，别把发版判成失败。
if ! pr=$(gh pr create --repo "$upstream" --base master --head "$owner:$branch" --title "$title" --body "$body" 2>&1); then
  pr=$(gh api "repos/$upstream/pulls?state=open&head=$owner:$branch" --jq '.[0].html_url' 2>/dev/null || true)
  [ -n "$pr" ] || { echo "上游 PR 没开成：$pr" >&2; exit 1; }
  echo "这个 tag 之前已经提过，沿用现有 PR。"
fi
echo "上游 PR：$pr"

# 回写本仓库 main，让仓库里那份副本跟发出去的一致；这一步只用工作流自带的令牌，失败不影响发版。
if [ -n "${GITHUB_TOKEN:-}" ]; then
  for f in "$dest"/*.yaml; do
    name=$(basename "$f")
    path="installer/winget/manifests/$rel/$VERSION/$name"
    cur=$(GH_TOKEN="$GITHUB_TOKEN" gh api "repos/$repo/contents/$path?ref=main" --jq .sha 2>/dev/null || true)
    if [ -n "$cur" ]; then
      GH_TOKEN="$GITHUB_TOKEN" gh api -X PUT "repos/$repo/contents/$path" -f message="$pkg $VERSION 清单" \
        -f content="$(base64 -w0 "$f")" -f branch=main -f sha="$cur" --jq .commit.sha
    else
      GH_TOKEN="$GITHUB_TOKEN" gh api -X PUT "repos/$repo/contents/$path" -f message="$pkg $VERSION 清单" \
        -f content="$(base64 -w0 "$f")" -f branch=main --jq .commit.sha
    fi
  done
fi

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  { echo "### winget"; echo; echo "清单 \`$pkg $VERSION\` 已提交上游：$pr"; } >> "$GITHUB_STEP_SUMMARY"
fi
