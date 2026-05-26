# WinForms Configuration Design Reference

This note records the UI direction learned from the EPSON RC+ 7.0 user guide and maps it to MDKOSS work items.

## Design Principles

EPSON RC+ is organized around a project-oriented engineering workflow:

- A project browser keeps programs, points, I/O labels, and configuration assets discoverable.
- Runtime tools are separate from configuration editors, such as Robot Manager, I/O Monitor, Task Manager, command window, and controller maintenance.
- System configuration uses a navigation tree on the left and focused configuration pages on the right.
- I/O is treated as a first-class subsystem, with hardware I/O, memory I/O, virtual I/O, labels, monitoring, and remote control concepts kept distinct.
- Backup, restore, diagnostics, and export are visible operational workflows rather than hidden file operations.

MDKOSS should keep its lightweight JSON-based configuration model, but present it through the same kind of engineering-software structure:

- Project and runtime summary always visible.
- Configuration pages grouped by subsystem.
- Online monitor pages separated from offline JSON editing.
- JSON import/export available at both whole-project and subsystem levels.
- Device internals exposed through labels, descriptions, status, and typed parameter editors instead of raw key-value text only.

## Proposed UI Structure

### Main Window

- Top command bar: setting file, run status, project, config, monitor, diagnostics.
- Left project explorer: project, drivers, devices, I/O, tasks, variables, logs.
- Center workspace: configuration and monitor tools.
- Bottom status/history panel: errors, warnings, recent events, runtime messages.

### Configuration Manager

- Left tree:
  - Project
  - Runtime
  - Drivers
  - Devices
  - I/O
  - Tasks
  - Variables
  - Import / Export
- Right detail page:
  - Grid or form editor for the selected subsystem.
  - Summary line showing counts and selected setting file.
  - Apply / Reload / Import / Export actions.

### Online Tools

- Device Manager: device state, driver connection, enabled flag, type, latest error.
- Task Manager: task name, type, interval, state, last run, CPU/load hint, pause/resume/stop actions.
- I/O Monitor: inputs, outputs, virtual I/O, labels, descriptions, live state, manual toggle where safe.
- I/O Label Editor: alias, direction, address, driver, description, tooltip text.
- Diagnostics: runtime history, export status snapshot, export support package.

## Task Breakdown

### Phase 1: Configuration Navigation

- [x] Add a unified WinForms component configuration manager.
- [x] Support whole-setting JSON import/export.
- [x] Support subsystem JSON import/export.
- [x] Replace tab-only navigation with a left configuration tree.
- [x] Add configuration summary/status line.
- [x] Keep legacy GPIO/Axis/Platform/Devs/Tasks buttons available during migration.

### Phase 2: I/O First-Class Editing

- [x] Add I/O Labels page with alias, direction, driver, address, description.
- [x] Convert GPIO/VIO parameters into structured I/O rows.
- [x] Preserve compatibility with existing `parameters` JSON shape.
- [x] Add import/export for I/O labels separately from devices.

### Phase 3: Runtime Monitoring Tools

- [x] Add Device Manager window for runtime device state.
- [x] Add Task Manager window for runtime task state.
- [x] Add I/O Monitor window with live refresh and label display.
- [x] Add runtime event/history panel.

### Phase 4: Project Operations

- [x] Add project backup/export action.
- [x] Add project restore/import action with validation.
- [x] Add diagnostics export for setting JSON, runtime snapshot, and logs.
- [x] Add validation before save for duplicate ids, missing drivers, invalid intervals, and malformed parameters.

### Phase 5: Typed Parameter Editors

- [x] Add driver-type-specific parameter editors.
- [x] Add device-type-specific parameter editors for GPIO, VIO, axis, platform, serial, TCP.
- [x] Add task-type-specific parameter editors.
- [x] Keep raw parameter editing as an advanced fallback.

## Implementation Status

The first implementation pass is complete in WinForms. The current version keeps the existing JSON schema and runtime model, then layers EPSON RC+-style tools over it:

- `ComponentConfigForm` now has configuration-tree navigation, whole-project import/export, backup, validation, I/O label editing, subsystem import/export, and parameter presets.
- `MainForm` now exposes Device Manager, Task Manager, I/O Monitor, Diagnostics export, and a runtime history panel.
- Runtime task state is exposed through a lightweight task snapshot API.

The parameter preset feature is intentionally conservative: it fills raw key-value parameters with type-specific templates while preserving the advanced raw parameter column for project-specific values.

## Immediate Implementation Notes

The first code change should keep the existing JSON schema and runtime model unchanged. The safest next step is to improve `ComponentConfigForm` layout only:

- Add a left `TreeView` that selects the existing pages.
- Keep the existing grids and row classes.
- Add a bottom `StatusStrip` with setting path and row counts.
- Avoid changing `MdkSetting` or runtime behavior in this phase.
