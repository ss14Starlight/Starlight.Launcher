%global debug_package %{nil}
%global __os_install_post %{nil}
%global _build_id_links none

Name:           starlight-launcher
Version:        %{pkg_version}
Release:        1%{?dist}
Summary:        Starlight Launcher for Space Station 14
License:        MIT
URL:            https://github.com/ss14Starlight/Starlight.Launcher
Source0:        Starlight.Launcher-linux-x64-%{version}.tar.gz
Source1:        icon.png
Source2:        starlight-launcher.desktop
ExclusiveArch:  x86_64

AutoReqProv:    no
Requires:       libwebkit2gtk-4.1.so.0()(64bit)

%description
A modern launcher for Space Station 14 by the Starlight Team.

%prep

%build

%install
install -d %{buildroot}/opt/starlight-launcher
tar -xzf %{SOURCE0} -C %{buildroot}/opt/starlight-launcher
chmod +x %{buildroot}/opt/starlight-launcher/Starlight.Launcher %{buildroot}/opt/starlight-launcher/loader/Robust.Loader
echo rpm > %{buildroot}/opt/starlight-launcher/install-kind

install -d %{buildroot}%{_bindir}
ln -s /opt/starlight-launcher/Starlight.Launcher %{buildroot}%{_bindir}/starlight-launcher

install -Dm644 %{SOURCE1} %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/starlight-launcher.png
install -Dm644 %{SOURCE2} %{buildroot}%{_datadir}/applications/starlight-launcher.desktop

%files
/opt/starlight-launcher
%{_bindir}/starlight-launcher
%{_datadir}/applications/starlight-launcher.desktop
%{_datadir}/icons/hicolor/256x256/apps/starlight-launcher.png
