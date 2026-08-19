# Iron Meridian — task runner
#
#   make            the menu of everything below
#   make installer  build the Windows player and package it as a setup .exe
#   make menu       pick a job interactively instead of naming one
#
# Every target hands off to scripts/menu.ps1, which holds the job table — so
# `make build` and the interactive picker cannot describe different work. To
# add a job, add a row there and its key to one of the lists below.
#
# GNU Make is not part of a Unity install; on Windows:
#   winget install --id ezwinports.make
# None of this is required — each job is a script you can run directly, and the
# menu itself runs without make (.\scripts\menu.ps1). See docs/35-TASKS.md.

# Recipes name the interpreter rather than relying on SHELL, so the same
# Makefile works whether make hands recipes to cmd.exe, sh or pwsh.
PWSH ?= pwsh
MENU = $(PWSH) -NoProfile -File ./scripts/menu.ps1

# Windows PowerShell 5.1 instead of PowerShell 7:  make PWSH=powershell <target>

BUILD_JOBS   := setup build installer package run
DATA_JOBS    := units icons stat-icons units-doc installer-art data
UNITY_JOBS   := models vfx packages
STEAM_JOBS   := steam-check steam-appid
PROJECT_JOBS := check packages-audit doctor logs clean distclean

JOBS := $(BUILD_JOBS) $(DATA_JOBS) $(UNITY_JOBS) $(STEAM_JOBS) $(PROJECT_JOBS)

.DEFAULT_GOAL := help
.PHONY: help menu $(JOBS)

## The menu, and the interactive picker
help:
	@$(MENU) -List

menu:
	@$(MENU)

## Everything else: one job, named
#
#   setup          generate the scenes and build settings (Unity, project closed)
#   build          build the Windows player into Builds\Windows
#   installer      build the player and package it as a setup .exe
#   package        package the player already in Builds\Windows
#   run            launch the built player
#   units          regenerate units.json
#   icons          regenerate the APP-6 unit icons
#   stat-icons     regenerate the unit info panel's stat glyphs
#   units-doc      rewrite the tables in docs/04-UNITS.md from units.json
#   installer-art  regenerate the installer icon and wizard bitmaps
#   data           all four generators, in dependency order
#   models         Unity: Install Unit Models
#   vfx            Unity: Install VFX Prefabs
#   packages       Unity: Import Bundled Packages
#   steam-check    release preflight: app id, icon, version, build, licences
#   steam-appid    write steam_appid.txt beside the player, to test Steam locally
#   check          compile every C# file with Roslyn, without opening Unity
#   packages-audit report which Unity packages nothing uses (-Apply strips them)
#   doctor         check Unity, Python, Pillow, Inno Setup and the token
#   logs           tail the last Unity setup and build logs
#   clean          delete the packaged installers and the build logs
#   distclean      delete everything under Builds\, player included (asks first)
#
# Options belong to the scripts, not to make — e.g. a signed installer, one that
# bundles the ion token, or a Steam upload, which has no target on purpose
# because every run of it is a decision:
#   .\scripts\build-installer.ps1 -IncludeToken
#   .\scripts\steam-upload.ps1 -Token Exclude -User <login> -Preview
$(JOBS):
	@$(MENU) -Run $@
