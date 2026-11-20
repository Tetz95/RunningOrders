# Tetz's Running Orders

A mod for Railroader (0.6.3 or later) that gives the Auto Engineer's waypoint mode a queue: line up waypoints for a
locomotive, each with its own orders (couple, cut, set brakes, refuel, wait, change speed or timetable symbol), and the
train works through them one after another. Queues can be saved as routes and sent to any locomotive.

Until 2026-11 it was called Waypoint Queue (Tetz fork). It began as Tetz95's fork of [PrinceOfPluto's Waypoint Queue](https://github.com/PrinceOfPluto/WaypointQueue) and is now
developed on its own. Thanks to PrinceOfPluto for the original; it is MIT-licensed, and its [LICENSE.txt](LICENSE.txt)
applies here too.

## Installing

Download the zip from [Releases](https://github.com/Tetz95/RunningOrders/releases) and install it with your mod manager,
or unpack it into the game's `Mods` folder (one folder, `Tetz's Running Orders`). It loads as `tetz95/running-orders` and can't
run alongside the original (`princeofpluto/waypointqueue`): install one or the other.

Settings: in the game's Settings window, *Running Orders* > *Open settings* (key bindings, coupling and cutting defaults,
hand brakes, speeds).

## What it does

- **A queue per locomotive.** Pick waypoints with the game's waypoint picker; they're added to the end of the queue (or
  inserted, or made next). The Waypoints window shows the queue, what each waypoint will do and how the train is doing.
- **Coupling** to a picked car, the nearest car on arrival, or a car you choose, then air and hand brakes,
  and optionally a cut right after: pick up or set out cars by count, destination, waybill status or a particular car.
- **Cutting** by count from either end, by destination (track, industry or area), at a particular car, by waybill status
  (spotted or not), or everything but the locomotives; with hand brakes, bled air or bottled air as you choose.
- **How the train stands.** The pose chosen in the picker (Space: which end of the train arrives on the mark, and on
  which side) is kept for every queued waypoint. When picking a waypoint later in the queue, the picker draws the train
  as it will be by then, after the earlier couplings and cuts.
- **Stopping or passing.** A waypoint can be passed without stopping, at a speed of your choice; the train is handed on
  to the next waypoint before it would brake.
- **Refueling** at a nearby coal, water or diesel loader, slot by slot.
- **Waiting** for a number of minutes, until a time of day, or until no car in the train is spotted.
- **Speed and timetable symbol** changes once a waypoint is reached.
- **Switches** the engineer threw are put back once the train is clear. Under CTC, a route that turns back beyond an
  interlocking runs clear of the plant first (setting: *Turn back clear of CTC interlockings*).
- **Saved routes** (the Routes window): save a queue as a route, sort routes into sections and order them, and send a
  route, one waypoint of it, or a waypoint and the rest to the selected locomotive. A locomotive can loop a route: when
  its queue runs empty, the route is added again.
- **Errors pause the queue.** If an order can't be carried out (a car to couple to is gone, say), the waypoint shows the
  error and the train waits until you resume or change it.

## Upgrading from 2.2.x

Running Orders is the new name of Waypoint Queue (Tetz fork): remove the old `WaypointQueue` folder from `Mods` (your
mod manager may do it: the two are marked incompatible). The versions after 2.2.x are a rewrite: queues in saves and saved routes from 2.2.x and earlier aren't read. Finish or note your queues,
and re-make your routes after updating. Settings and key bindings carry over.

## Reporting a problem

Open an [issue](https://github.com/Tetz95/RunningOrders/issues) with what happened, what you expected, and your
`Player.log` (`%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\Player.log`, right after the problem). When a
waypoint shows an error, its *View error* button has a link here too.

## Building

The projects are SDK-style: `dotnet build RunningOrders/RunningOrders.csproj` builds without Visual Studio. Copy
`Paths.user.example` to `Paths.user` and set `RrInstallDir` (the game folder) and `RrHomeDir` (the folder whose `Mods`
the build installs into, normally `Documents\Railroader`); `-p:NoInstall=true` builds into `bin\` instead.

Tests: `dotnet test RunningOrders.Tests/RunningOrders.Tests.csproj`. They run outside the game: the cut selection
(which cars a cut takes) works on plain lists of cars, so the tests use simple test cars.

Code layout:
- `Model\` - a waypoint is the player's order (`WaypointOrder`, never changed by the mod) plus its progress
  (`WaypointProgress`, saved with the queue so a reload carries on).
- `Steps\` - what happens on arrival, one step at a time: start, couple, refuel, halt, cut, speed, wait.
- `Services\` - the game side of coupling, cutting and refueling; `Services\CutSelection.cs` is the pure part.
- `Picker\` - the game's waypoint picker: poses, and the train drawn as it will be.
- `UI\` - the Waypoints window (`WaypointWindow.*.cs`, one file per section), the Routes window and the settings.

Releasing: set the version in `RunningOrders/Properties/AssemblyInfo.cs`, build with
`dotnet build RunningOrders/RunningOrders.csproj -c Release -p:NoInstall=true -o <out>`, package with
`python tools/package.py . <out> "Tetz's Running Orders.zip"` (one top folder `Tetz's Running Orders`), tag `v<version>`
and attach the zip to a GitHub release under that name (mod managers then offer it as an update).
