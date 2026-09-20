DOTNET ?= dotnet
CONFIGURATION ?= Release

.PHONY: help build clean clean-win clean-mac publish publish-win publish-mac

help:
	@echo "ALLS build targets"
	@echo "  make build         Build the complete solution"
	@echo "  make clean         Remove bin, obj, and packaged artifacts"
	@echo "  make publish       Publish on the current Windows/macOS host"
	@echo "  make publish-win   Publish with PowerShell on Windows"
	@echo "  make publish-mac   Publish with bash and ditto on macOS"
	@echo ""
	@echo "Overrides: DOTNET=/path/to/dotnet CONFIGURATION=Release"

build:
	"$(DOTNET)" build ALLS.sln -c "$(CONFIGURATION)" -m:1

ifeq ($(OS),Windows_NT)
publish: publish-win
clean: clean-win
else
publish: publish-mac
clean: clean-mac
endif

clean-win:
	powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/clean.ps1

clean-mac:
	bash scripts/clean.sh

publish-win:
	powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-win-x64.ps1 -DotnetPath "$(DOTNET)" -Configuration "$(CONFIGURATION)"

publish-mac:
	DOTNET_CMD="$(DOTNET)" CONFIGURATION="$(CONFIGURATION)" bash scripts/publish-win-x64.sh
