# Repository instructions

Oops, Nice Shot! is a physics-based minigolf puzzle game. The player changes the course and the golfer hits the ball. The current Level1 is an object-placement sandbox. See [PROJECT_DOCUMENTATION.md](PROJECT_DOCUMENTATION.md) for the current architecture, asset layout, serialized wiring, and implementation status.

- Consult the relevant documentation before implementing features, fixing complex bugs, or changing architecture. Inspect the relevant source and serialized assets before editing.
- Treat current code and assets as authoritative for implementation. Use design intent as guidance and distinguish planned features from implemented behavior.
- Follow existing architecture and conventions. Reuse systems and avoid unnecessary complexity, duplication, and unrelated edits. Preserve gameplay and visual behavior unless explicitly requested otherwise.
- Preserve GUIDs, `.meta` files, serialized fields, Inspector values, scene, prefab and material references, and import settings. Check shader includes, assembly definitions, `Resources` paths, and other path-dependent systems when moving files.
- Obtain explicit approval before deleting assets or performing major refactoring. Ignore generated folders such as `Library/`, `Temp/`, `Logs/`, `obj/`, build outputs and caches, and skip unnecessary third-party source.
- Update the relevant documentation and implementation status after significant gameplay, architecture, dependency, shader, or configuration changes.
- Validate where possible. Distinguish static analysis from runtime verification and report assumptions, risks, and manual tests still required.

## Asset organization

- Put first-party assets under `Assets/MiniGolf/`. Group scripts by system, environment assets by type under `Art/`, effect shaders and materials under `Rendering/`, and configuration under `Settings/`. Keep scenes in `Scenes/` and Editor-only scripts in `Scripts/Editor/`.
- Use descriptive PascalCase names for new assets. Keep component and ScriptableObject filenames matched to their classes and keep partials together. Create folders when the first authored assets need them.
- Keep imported vendor trees in place, including TextMesh Pro resources and licenses. For approved moves, preserve asset and folder metadata and check build paths, shader includes, importer remaps, and other path-dependent references.

## Unity MCP

- The local Editor integration is MCP for Unity. See [Unity tooling and verified baseline](PROJECT_DOCUMENTATION.md#unity-tooling-and-verified-baseline) for the installed integration and project baseline.
- Before Editor operations, verify the MCP connection, available tools, and that the open Unity Editor is on the same project and working copy. Use only available tools. Prefer direct repository reads for C# and shader source. Use MCP selectively for scenes, prefabs, components, Inspector values, serialized references, and asset relationships.
- For authorized moves and renames, prefer Unity Editor asset operations that preserve GUIDs, metadata, import settings, and serialized data. If a filesystem fallback is needed, obtain approval and coordinate closing Unity first.
- Use MCP for relevant compilation, Console and reference checks, asset validation, and authorized tests. Profile when justified. Check references beyond the active scene before drawing conclusions about usage.
- Keep analysis-only tasks read-only and respect task-specific approval gates. Obtain authorization to save or change assets or settings, enter Play Mode, or run tests that change project state. Preserve unsaved user work.
- Minimize tool calls and response sizes. Report unavailable or unsupported operations and propose a safe alternative, such as read-only source and YAML inspection. Label actual Editor verification separately from static findings. Obtain approval before installing or configuring additional packages.
- These standing rules apply within each task's authorized scope and permit future authorized implementation. Preserve this MCP policy when updating this file.
