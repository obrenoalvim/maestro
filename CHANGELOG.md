# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.0] - 2026-11-01

### Added
- Game mode with no list of games: follows the Game Bar presence signal, the games Windows has seen and the ones marked by hand in the Game Bar. The game gets high priority, torrent clients are held back, qBittorrent's alternative speed limit turns on through its local Web UI and Medal starts hidden; all of it is undone when the game closes.
- Background balancer: programs that hog the CPU behind the window in front go to below normal priority, efficiency mode and low disk priority, and come back the instant their window is brought forward.
- Programs moving more than 20 MB/s get above normal priority while no game runs.
- Settings window in English and Portuguese with Home, FPS test, Games, Medal and qBittorrent, and General pages.
- FPS overlay (`Ctrl+Alt+F`) and a with/without comparison that measures average FPS, 1% low and worst frame time, with an option to simulate a heavy background program. Frames come from Intel PresentMon, downloaded on first use and checked by SHA-256.
- Journal of every change, undone on pause, exit or the next start after a crash.
- A broken settings file keeps the last version that worked.
- Start menu shortcut, start with Windows through the same `StartupApproved` switch as Task Manager, and an optional notice when a new version is out.
