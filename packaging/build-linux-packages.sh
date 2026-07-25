#!/usr/bin/env bash
set -euo pipefail

runtime="${1:-}"
version="${2:-}"
publish_dir="${3:-}"
output_dir="${4:-}"

if [[ ! "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
	echo "Version must be a semantic version without a leading v." >&2
	exit 2
fi

case "${runtime}" in
	linux-x64)
		debian_arch="amd64"
		appimage_arch="x86_64"
		;;
	linux-arm64)
		debian_arch="arm64"
		appimage_arch="aarch64"
		;;
	*)
		echo "Runtime must be linux-x64 or linux-arm64." >&2
		exit 2
		;;
esac

if [[ -z "${publish_dir}" || ! -x "${publish_dir}/GraphSketcher" ]]; then
	echo "Publish directory must contain an executable GraphSketcher file." >&2
	exit 2
fi
if [[ -z "${output_dir}" ]]; then
	echo "An output directory is required." >&2
	exit 2
fi

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
publish_dir="$(cd -- "${publish_dir}" && pwd)"
mkdir -p "${output_dir}"
output_dir="$(cd -- "${output_dir}" && pwd)"
work_dir="$(mktemp -d "${TMPDIR:-/tmp}/graphsketcher-linux-package.XXXXXX")"
trap 'rm -rf -- "${work_dir}"' EXIT

package_name="GraphSketcher-Linux-v${version}-${runtime}"
portable_dir="${work_dir}/${package_name}"
mkdir -p "${portable_dir}"
cp -a "${publish_dir}/." "${portable_dir}/"
install -m 0644 \
	"${repo_root}/LICENSE" \
	"${repo_root}/NOTICE.md" \
	"${repo_root}/THIRD-PARTY-NOTICES.md" \
	"${repo_root}/README.md" \
	"${repo_root}/ROADMAP.md" \
	"${portable_dir}/"
cp -a "${repo_root}/docs" "${portable_dir}/docs"
chmod 0755 "${portable_dir}/GraphSketcher"

tar \
	--sort=name \
	--mtime="@0" \
	--owner=0 \
	--group=0 \
	--numeric-owner \
	-C "${work_dir}" \
	-czf "${output_dir}/${package_name}.tar.gz" \
	"${package_name}"

install_application_layout() {
	local root="$1"
	install -d \
		"${root}/usr/bin" \
		"${root}/usr/lib/graphsketcher" \
		"${root}/usr/share/applications" \
		"${root}/usr/share/icons/hicolor/512x512/apps" \
		"${root}/usr/share/metainfo" \
		"${root}/usr/share/mime/packages" \
		"${root}/usr/share/doc/graphsketcher"
	cp -a "${publish_dir}/." "${root}/usr/lib/graphsketcher/"
	chmod 0755 "${root}/usr/lib/graphsketcher/GraphSketcher"
	install -m 0644 \
		"${repo_root}/packaging/linux/io.github.jacoba1100254352.GraphSketcher.desktop" \
		"${root}/usr/share/applications/"
	install -m 0644 \
		"${repo_root}/src/GraphSketcher.App/Assets/GraphSketcher.png" \
		"${root}/usr/share/icons/hicolor/512x512/apps/io.github.jacoba1100254352.GraphSketcher.png"
	install -m 0644 \
		"${repo_root}/packaging/linux/io.github.jacoba1100254352.GraphSketcher.metainfo.xml" \
		"${root}/usr/share/metainfo/"
	install -m 0644 \
		"${repo_root}/packaging/linux/io.github.jacoba1100254352.GraphSketcher.xml" \
		"${root}/usr/share/mime/packages/"
	install -m 0644 \
		"${repo_root}/LICENSE" \
		"${repo_root}/NOTICE.md" \
		"${repo_root}/THIRD-PARTY-NOTICES.md" \
		"${root}/usr/share/doc/graphsketcher/"
	cat >"${root}/usr/bin/graphsketcher" <<'LAUNCHER'
#!/usr/bin/env bash
set -euo pipefail
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${XDG_CACHE_HOME:-${HOME}/.cache}/graphsketcher/bundle"
exec /usr/lib/graphsketcher/GraphSketcher "$@"
LAUNCHER
	chmod 0755 "${root}/usr/bin/graphsketcher"
}

deb_root="${work_dir}/deb-root"
install_application_layout "${deb_root}"
install -d "${deb_root}/DEBIAN"
installed_size="$(du -sk "${deb_root}/usr" | awk '{print $1}')"
cat >"${deb_root}/DEBIAN/control" <<CONTROL
Package: graphsketcher
Version: ${version}
Section: education
Priority: optional
Architecture: ${debian_arch}
Installed-Size: ${installed_size}
Maintainer: GraphSketcher Linux contributors <noreply@github.com>
Depends: libx11-6, libx11-xcb1, libice6, libsm6, libfontconfig1, libfreetype6, libxext6, libxcb1, libxrender1, libgl1, libdbus-1-3, zlib1g
Homepage: https://github.com/Jacoba1100254352/GraphSketcher.Linux
Description: Direct-manipulation graph sketching and data plotting
 GraphSketcher creates publication-ready graphs from direct drawing or
 tabular data and supports the portable graphsketch document format.
CONTROL
cat >"${deb_root}/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e
command -v update-mime-database >/dev/null 2>&1 && update-mime-database /usr/share/mime || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database /usr/share/applications || true
POSTINST
cat >"${deb_root}/DEBIAN/postrm" <<'POSTRM'
#!/bin/sh
set -e
command -v update-mime-database >/dev/null 2>&1 && update-mime-database /usr/share/mime || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database /usr/share/applications || true
POSTRM
chmod 0755 "${deb_root}/DEBIAN/postinst" "${deb_root}/DEBIAN/postrm"
dpkg-deb \
	--root-owner-group \
	--build "${deb_root}" \
	"${output_dir}/GraphSketcher-Linux-v${version}-${debian_arch}.deb"

if [[ -n "${APPIMAGETOOL_PATH:-}" && "${runtime}" == "linux-x64" ]]; then
	if [[ ! -x "${APPIMAGETOOL_PATH}" ]]; then
		echo "APPIMAGETOOL_PATH is not executable." >&2
		exit 2
	fi
	app_dir="${work_dir}/GraphSketcher.AppDir"
	install_application_layout "${app_dir}"
	install -m 0755 "${repo_root}/packaging/linux/AppRun" "${app_dir}/AppRun"
	ln -s "usr/share/applications/io.github.jacoba1100254352.GraphSketcher.desktop" \
		"${app_dir}/io.github.jacoba1100254352.GraphSketcher.desktop"
	ln -s "usr/share/icons/hicolor/512x512/apps/io.github.jacoba1100254352.GraphSketcher.png" \
		"${app_dir}/io.github.jacoba1100254352.GraphSketcher.png"
	ln -s "io.github.jacoba1100254352.GraphSketcher.png" "${app_dir}/.DirIcon"
	ARCH="${appimage_arch}" APPIMAGE_EXTRACT_AND_RUN=1 \
		"${APPIMAGETOOL_PATH}" \
		"${app_dir}" \
		"${output_dir}/GraphSketcher-Linux-v${version}-${appimage_arch}.AppImage"
	chmod 0755 "${output_dir}/GraphSketcher-Linux-v${version}-${appimage_arch}.AppImage"
fi

echo "Created Linux packages in ${output_dir}"
