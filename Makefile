.PHONY: build test publish-linux publish-windows

build:
	NUGET_CERT_REVOCATION_MODE=offline DOTNET_SYSTEM_NET_DISABLEIPV6=1 DOTNET_ROLL_FORWARD=Major dotnet build Client.Desktop/Client.Desktop.csproj

test:
	NUGET_CERT_REVOCATION_MODE=offline DOTNET_SYSTEM_NET_DISABLEIPV6=1 DOTNET_ROLL_FORWARD=Major dotnet test Client.Desktop.Tests/Client.Desktop.Tests.csproj

publish-linux:
	NUGET_CERT_REVOCATION_MODE=offline DOTNET_SYSTEM_NET_DISABLEIPV6=1 DOTNET_ROLL_FORWARD=Major dotnet publish Client.Desktop/Client.Desktop.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForExtract=true -o out/linux

publish-windows:
	NUGET_CERT_REVOCATION_MODE=offline DOTNET_SYSTEM_NET_DISABLEIPV6=1 DOTNET_ROLL_FORWARD=Major dotnet publish Client.Desktop/Client.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForExtract=true -o out/windows
