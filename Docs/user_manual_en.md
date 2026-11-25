# StitchMaker User Manual

StitchMaker is a Unity Editor extension that lets you intuitively draw stitch paths along the surface of 3D models (such as shoes, clothing, gloves, and accessories) and GPU-bake them directly into Color, Normal, Height, and Mask textures.

Supported Environments: Unity 2022.3.22f1 or later / Unity 6

---

## Table of Contents

1. [Quick Start](#quick-start)
   - [Step 1: Open StitchMaker](#step-1-open-stitchmaker)
   - [Step 2: Assign the Target Mesh (Renderer)](#step-2-assign-the-target-mesh-renderer)
   - [Step 3: Create a Path and Draw Stitches in the Scene](#step-3-create-a-path-and-draw-stitches-in-the-scene)
   - [Step 4: Select a Preset](#step-4-select-a-preset)
   - [Step 5: Export Textures](#step-5-export-textures)
2. [Detailed Options](#detailed-options)
   - [Window Header](#window-header)
   - [TARGET](#target)
   - [PATHS & Scene Interaction](#paths--scene-interaction)
   - [STITCH SETTINGS](#stitch-settings)
   - [Island Constrain](#island-constrain)
   - [Mirror Function](#mirror-function)
   - [HOLES & SINK (AO)](#holes--sink-ao)
   - [PREVIEW / EXPORT](#preview--export)
3. [Troubleshooting & FAQ](#troubleshooting--faq)

---

## Quick Start

A minimal, step-by-step guide to generating stitch textures without worrying about fine-grained settings.

### Step 1: Open StitchMaker
In the Unity top menu, navigate to **`dennokoworks` > `StitchMaker`** to open the tool window.

### Step 2: Assign the Target Mesh (Renderer)
1. Drag and drop the avatar's or garment's **SkinnedMeshRenderer** or **MeshRenderer** from your scene into the **`Renderer`** field.
2. If the target mesh contains multiple materials, select the desired submesh slot from the **`Material`** dropdown.

> [!NOTE]
> The FBX import setting for the target model must have **`Read/Write Enabled`** checked.

### Step 3: Create a Path and Draw Stitches in the Scene
1. Click the **`+ Add Path`** button (or simply left-click on the mesh surface in the Scene view while Edit Mode is active to automatically create a path).
2. Ensure the **`Edit Mode`** button is highlighted in blue (active).
3. **Left-click on the mesh surface in the Scene view** to place stitch points (nodes):
   - **Left-Click**: Add control points on the mesh surface.
   - **Click on Line**: Insert a new control point into an existing segment.
   - **Drag**: Move a control point along the surface.
   - **Shift + Click / Drag on empty space (box select)**: Select multiple control points and move them together.
   - **Right-Click**: Delete the clicked control point.

### Step 4: Select a Preset
1. In the **`STITCH SETTINGS`** card, click the **`Apply…`** button under **`Preset`**.
2. A list with thumbnails opens. Check the look and click a stitch style (e.g., `Straight`, `Double`, or `DoubleNarrow` (closely spaced double line) under `Basic`, `Cross` under `Decorative`, `Channel` (stitches sunk in a groove) under `Special`, etc.).
   - Each thumbnail is scaled automatically to show its stitch clearly, so thumbnails cannot be used to compare actual sizes between presets. Hover over a tile to see the actual length, width, and spacing.
3. Adjust the **`Thread Color`** if desired.

### Step 5: Export Textures
1. Check the real-time preview on your mesh in the Scene view (rendered automatically when **`Preview`** is enabled).
2. Select your desired **`Resolution`** (default: `2048`).
3. Click the **`Export Textures`** button at the bottom of the window.
4. The generated `Color`, `Normal`, `Height`, and `Mask` PNG texture files will be saved in your project folder.

---

## Detailed Options

### Window Header

| Item | Description |
| :--- | :--- |
| **Version Display / ↻** | Shows the current version of StitchMaker. Click `↻` to check for updates. |
| **JA / EN Buttons** | Toggles the UI display language between Japanese (JA) and English (EN). Defaults to your operating system language. |

---

### TARGET

Specifies the 3D model onto which stitches will be projected and baked.

- **Renderer**: Assign a `SkinnedMeshRenderer` or `MeshRenderer` (with a `MeshFilter`).
- **Clear**: Detaches the assigned Renderer. Because paths are placed on that mesh, **clearing it also resets the path list to a new document** (you are asked first if there are unsaved changes). The same reset happens if the target is missing after a script compile or similar reload.
- **Material**: Selects which material/submesh slot on the target Renderer to draw stitches on. The default export directory is automatically inferred from the selected material's location.
- **Reload Mesh**: Refreshes the cached mesh data. Click this if you have modified vertex positions, updated mesh imports, or toggled Read/Write settings.

> [!IMPORTANT]
> **Mesh Requirements:**
> - The FBX model must have **`Read/Write Enabled`** checked in its Model Import Settings.
> - The mesh must contain valid **`UV0`** texture coordinates.
> - Only triangle topology is supported.

---

### PATHS & Scene Interaction

Manage multiple stitch lines and edit them directly in the Unity Scene view.

#### Document Management
- **New**: Clears all current paths and settings to start a blank document.
- **Load**: Loads a previously saved StitchMaker path asset (`.asset`).
- **Overwrite**: Saves straight to the `.asset` you loaded (or saved before), without choosing a file. For a new document that has never been saved, it asks where to save, like **Save As**.
- **Save As**: Saves all current paths, nodes, and export settings as an `.asset` file at a location you choose. Later **Overwrite** saves go to this file. Useful for reusing stitch lines across outfits or creating backups.
- Below the buttons, the window shows the file you are editing (where **Overwrite** saves) and whether there are unsaved changes.

> [!NOTE]
> Changes are not saved to the `.asset` automatically. Unsaved changes are lost when you close the window or quit Unity.

#### Path List
- **Selecting a Path**: Click a path name in the list to select it for editing.
- **Multiple Selection**: **Ctrl + Click** (Cmd + Click on Mac) adds or removes one path at a time, and **Shift + Click** selects the range from the path you clicked last. The stitch settings panel shows the path you clicked last (bold); the other selected paths have a blue outline. Adding, deleting, or duplicating paths, or using Undo, returns to a single selection.
- **Eye Icon (Show / Hide)**: Toggles path visibility. Hidden paths are excluded from both the Scene view preview and texture export.
- **× Button**: Deletes the selected path.
- **Right-Click Menu**: Right-click a path row for the following:
  - **Duplicate**: Duplicates the path including its shape, stitch settings, mirror settings, and Island Constrain, and inserts it right after the original.
  - **Copy / Paste Stitch Settings**: Copies the stitch settings (mask, scale, length, width, thread color, groove, etc.) to another path. The shape, mirror settings, and Island Constrain are not changed. **Choosing Paste on a row that is part of a multiple selection pastes into all selected paths at once** (on a row that is not selected, only that row). Copied settings are kept until Unity is closed or scripts are recompiled.
- **+ Add Path**: Appends a new stitch path to the list. If a path is selected, the new path inherits its stitch settings (mask, scale, length, width, thread color, etc.), which is handy for placing the same stitch in several places. The shape (control points, Closed, Bezier Handles), mirror settings, and Island Constrain are not inherited (Island Constrain starts ON).

#### Path Editing Controls
- **Edit Mode**: Toggles the interactive editing mode in the Scene view.
  - **ON**: Suppresses standard Scene view object selection so you can click and drag on the mesh to edit paths without accidentally selecting other GameObjects. A green HUD guide appears in the lower-right corner of the Scene view.
  - **OFF**: Restores standard Unity Scene view interaction (selecting and transforming other GameObjects).
- **Closed**: When checked, connects the start and end nodes into a closed loop (ideal for collars, cuffs, hems, etc.).
- **Bezier Handles**:
  - **OFF (Automatic Spline)**: Control points are automatically interpolated into smooth curves (Catmull-Rom based). This is the fastest and most intuitive workflow.
  - **ON (Manual Bezier)**: Displays pink tangent handles for each node. Dragging handle endpoints allows precise control over curvature and direction.

#### Mouse Controls in Scene View

| Action | Function |
| :--- | :--- |
| **Left Click on Mesh** | Adds a new control point at the clicked position (at the end of the path or after the selected node). |
| **Left Click on Path Line** | Inserts a new control point at the clicked segment. |
| **Drag Control Point** | Moves the control point along the mesh surface (dragging one of several selected points moves all selected points together). |
| **Shift + Left Click on Control Point** | Adds the control point to the selection (removes it if already selected). |
| **Left Drag on Empty Space** | Box select (hold Shift to add to the current selection). Dragging from an empty spot on the mesh does not add a control point. |
| **Left Click outside the Mesh** | Clears the selection. |
| **Drag Bezier Handle** | (When Bezier Handles is ON) Adjusts the direction and curvature of the curve. |
| **Right Click on Control Point** | Deletes the clicked control point. |
| **Ctrl + Left Click / Drag** | On a path with Island Constrain, places the control point on the surface in front instead of preferring the path's part behind it (see [Island Constrain](#island-constrain)). |
| **Alt + Left / Right Drag** | Standard Unity scene camera rotation and zoom (functions normally without interrupting path editing). |

---

### STITCH SETTINGS

Configures the appearance and placement of stitches for the currently selected path.

#### Presets
- **Apply…**: Opens a list of built-in and custom user presets with thumbnails, and applies the one you click to the selected path. Built-in presets are grouped by category, and your own presets are listed under "Saved".
- **Delete…**: Deletes a saved custom preset.
- **Folder**: Opens the user preset directory (`%APPDATA%/dennokoworks/StitchMaker/Presets`) in File Explorer / Finder for easy backup and sharing.
- **Preset Name & Save**: Saves the current stitch parameters and mask texture as a preset under the specified name.

#### Parameters

| Parameter | Range / Unit | Description |
| :--- | :--- | :--- |
| **Name** | String | Name of the path. |
| **Mask** | Texture2D | Grayscale decal mask defining the stitch shape. White represents thread/height, black is background. *Paths without a mask will not be rendered.* |
| **Scale** | 0.25 – 4× | Multiplier applied to Length, Width, Spacing, and Jitter: Position together. Changes the overall size while keeping the stitch proportions. The fields below show values before scaling. Applying a preset resets it to 1.0 (custom presets saved with a scale restore that scale). |
| **Length (mm)** | 0.5 – 20 mm | Length of each individual stitch along the tangent direction. |
| **Width (mm)** | 0.1 – 10 mm | Width of each individual stitch. |
| **Spacing (mm)** | 0.5 – 30 mm | Distance (pitch) between stitch centers along the path. Matching this to Length yields continuous stitches; higher values create dashed stitches. |
| **Angle (°)** | -180° – 180° | Angular rotation offset relative to the path tangent. Useful for diagonal or cross-stitched effects. |
| **Thread Color** | Color (RGB) | Color of the thread applied to the Color texture output. |
| **Height** | 0 – 1 | Height scale for the stitch in the Height texture. |
| **Normal Strength** | 0 – 2 | Intensity of bumpiness in the Normal map. |
| **Groove Width (mm)** | 0 – 10 mm | Width of a groove pressed into the fabric along the path. 0 means no groove. Use it for stitches sunk in a channel, as seen around the welt of shoes (preset `Channel`). Scale also applies. |
| **Groove Depth** | 0 – 2 | Steepness of the groove walls. The groove appears only in the Normal map and the AO, and does not affect Color or Height (the fabric is at height 0 in the Height map, so nothing can go below it). |
| **Jitter: Position (mm)** | 0 – 2 mm | Adds random positional variation to each stitch for a hand-stitched, organic look. |
| **Jitter: Angle (°)** | 0 – 30° | Adds random angular variation to each stitch. |
| **Seed** | Integer | Random seed value for reproducible jitter generation. Changing this value produces a different jitter pattern. |

---

### Island Constrain

When you draw a stitch where **separate parts overlap within a few millimeters** (laces over a shoe upper, a collar over the body, a pocket over the fabric, etc.), the stitch can also be printed onto the part lying on top.
With **Island Constrain** enabled (above Mirror Axis in the stitch settings, ON by default), stitches are drawn only on the parts (UV islands) the path lies on, so they do not leak onto overlapping parts.

- Paths that cross a UV seam are drawn without gaps on the islands on both sides of the seam. The same applies to the mirrored side.
- In the Scene view, once the path has at least one control point, clicks place control points on the path's part even if another part (such as a lace) is in front, as long as the path's surface is within 1 cm behind it. Hold **Ctrl** while clicking / dragging to place a point on the part in front.
- This is a per-path setting and is not part of presets (applying a preset does not change it).
- Turn it OFF to draw on every nearby surface as before, e.g. when a stitch should intentionally span separate parts.

> [!NOTE]
> If a section whose two end points lie on the same part crosses another part on the way, the crossing portion is not drawn. Place a control point on the crossed part as well, or turn Island Constrain OFF.

---

### Mirror Function

Used for symmetrical models (shoes, jackets, gloves, sleeves) to mirror stitches across an axis in 3D space.
Because StitchMaker mirrors in **3D mesh space** and re-projects onto the opposite 3D surface, it works seamlessly with avatar models whose left and right sides use distinct, non-overlapping UV islands.

| Setting | Description |
| :--- | :--- |
| **Mirror Axis** | Selects the axis of symmetry (`None`, `X`, `Y`, `Z`). For standard humanoid models, select **`X`** (local left-right axis). When active, a ghosted preview of the mirrored path appears in the Scene view. |
| **Snap Radius (mm)** | Search distance (default: `30 mm`) for snapping mirrored control points to the opposite mesh surface. Ensures stitches remain perfectly aligned even if the mesh has sculpted asymmetry, wrinkles, or draping differences. |
| **Bake Mirror to Path** | Converts the dynamic mirrored path into a **new, independent path** in the path list, and resets the original path's mirror setting to `None`. Use this if you need to manually fine-tune individual nodes on the mirrored side or create an asymmetrical accent. |

> [!WARNING]
> **Overlapping UVs Caution:**  
> For low-poly models where the left and right sides share (overlap) the exact same UV islands to save texture space, mirroring in 3D will bake the stitches twice onto the same UV coordinates. **Turn the mirror OFF for meshes whose left and right sides share (overlap) the same UV islands.**

---

### HOLES & SINK (AO)

On leather, the holes the thread passes through are recessed and dark, and the leather around the thread sinks slightly where it is pulled tight. The **HOLES & SINK (AO)** card (below the stitch settings) draws these dents into the **Normal map** and an **AO (shadow) texture**.
Because they are not always wanted (for example on fabric stitches), **holes and sink are both OFF by default** (all bundled presets keep them OFF). Click the card heading to collapse it.

| Parameter | Unit / Range | Description |
| :--- | :--- | :--- |
| **Holes** | ON / OFF | Draws the holes where the thread enters the leather as dents and shadows. **The hole positions are found from the mask automatically** (the ends of the thread and the tips of turns, as in a zigzag). |
| **Hole Size** | 0.2 – 4 | Hole size relative to the thread thickness. At 1, a hole as wide as the thread opens just past the thread tip. |
| **Hole Depth** | 0 – 1 | Darkness of the hole shadows and strength of the dents. Normal Strength also applies to the Normal map. |
| **Sink** | ON / OFF | Draws the fabric sinking around the thread where it is pulled tight, following the thread's shape. |
| **Sink Width** | 0 – 5 mm | How far the sink spreads from the edge of the thread. It fades smoothly back to the surface over this distance. Widening it does not make the dent at the thread edge shallower. Scale also applies. |
| **Sink Depth** | 0 – 1 | Darkness of the shadow and strength of the dent at the thread's edge. Normal Strength also applies to the Normal map. |

- The AO also contains the shadow of **grooves** (linked to Groove Depth), in addition to holes and sink.
- **Thread areas get no AO** (white). The holes open just past the thread tips, so the thread does not hide them.
- The Height map does not change (the fabric stays at height 0).
- These settings are saved in presets.
- Your own masks also get holes automatically, as long as the thread ends are round (or V-shaped). If Holes is ON but no positions are found, the card shows "No hole positions were found in this mask."
- Among the bundled masks, **Overlock** gets no holes because its needle lands on top of the needle thread. **Blanket** gets a hole only at the lower end of the vertical thread, and **Chain** only at the pointed end of each loop.

> [!NOTE]
> Where the sinks of neighboring stitches overlap, the dents in the Normal map are not added together; the steeper one is used. With tight spacing and a wide sink, the dent between stitches can look shallower.

---

### PREVIEW / EXPORT

#### Real-time Preview
- **Preview Toggle**: Enables or disables the real-time stitch overlay in the Scene view.
- **Display Modes**:
  - **Combined**: Full shading preview showing colors, normals, and lighting combined.
  - **Normal**: Visualizes the tangent-space normal map.
  - **Height**: Visualizes the grayscale height map.
  - **AO**: Visualizes the shadows of holes, sink, and grooves in grayscale (white = no shadow). Combined also overlays these shadows where there is no thread.

#### Export Settings
- **Resolution**: Output texture resolution (`512`, `1024`, `2048`, `4096`). Matching your garment's original texture resolution is recommended.
- **Padding (px)**: Dilates pixels beyond the decal border (default: `4 px`) to prevent dark seams and mipmap bleeding.
- **Anti-aliasing**: On export, renders at 2x / 4x the width and height and then scales down, smoothing jagged edges on stitch outlines and curves (default: `2x`).
  - **OFF**: Renders at the output resolution. Fastest.
  - **2x**: Recommended for most cases.
  - **4x**: Smoother still, but exporting takes longer and uses more VRAM.
  - The internal working resolution is capped at `8192 px`. If a combination exceeds it (e.g. `4096` with `4x`), textures are exported with `2x` instead, and a note appears below the setting.
  - Anti-aliasing is applied only when exporting textures. To keep editing responsive, it is not reflected in the Scene view preview.
- **Export Checkboxes**:
  - **Color**: RGBA base color texture with alpha transparency.
  - **Normal**: Normal map texture (automatically imported as Texture Type `Normal map` in Unity).
  - **Height**: Grayscale height map (linear).
  - **Mask**: Grayscale coverage mask (white = stitch, black = background).
  - **AO**: Shadows of holes, sink, and grooves (white = no shadow; thread areas are white). Assign it to a material's AO (Occlusion) slot or multiply it onto the base map. OFF by default.
- **Overwrite**:
  - **ON**: Overwrites existing files with the same name.
  - **OFF**: Appends sequential numbers (`_1`, `_2`, etc.) to avoid overwriting existing textures.
- **Transparent Outside Stitches**:
  - **ON**: Normal and Height maps will also have transparency outside stitches (Color is always transparent). Perfect for layering on top of existing textures in image editing software. For paths with a groove, holes, or sink, the dents remain visible in the Normal map even where there is no thread. The AO is exported as black + alpha (shadow strength), so you can bake the shadows in just by placing it as a normal layer on top of the base map.
    - *Note: Unity's "Normal map" texture type does not treat alpha as transparency, so when this option is ON the Normal map is saved with Texture Type "Default" (sRGB OFF, Alpha Is Transparency ON). If you want to use it directly as a normal map in a shader inside Unity, change its Texture Type to "Normal map" in the import settings.*
  - **OFF**: Fills non-stitch background with neutral normal color (#8080FF), height black (#000000), and AO white (#FFFFFF).
- **Output Folder**: Destination folder inside your project (`Assets/...`). Click the `...` button to choose a folder via file dialog.
- **Export Textures**: Executes GPU baking and saves the PNG files to the specified folder. Once complete, Unity highlights the destination folder in the Project window.

#### Output Naming Convention
Textures are exported using the following format:
- `{RendererName}_Stitch_Color.png`
- `{RendererName}_Stitch_Normal.png`
- `{RendererName}_Stitch_Height.png`
- `{RendererName}_Stitch_Mask.png`
- `{RendererName}_Stitch_AO.png`

---

## Troubleshooting & FAQ

### Q. Error: "Cannot read the mesh vertices (enable Read/Write)"
**A.** Read/Write is disabled on the 3D model asset.
1. Select the original 3D model file (e.g., FBX) in the Project window.
2. Open the **`Model`** tab in the Inspector.
3. Check **`Read/Write Enabled`** (or `Read/Write`) and click **`Apply`**.
4. In the StitchMaker window, click **`Reload Mesh`**.

### Q. Error: "The mesh has no UV0"
**A.** The model does not contain primary UV texture coordinates (UV0). UV unwrap the model in a 3D modeling tool like Blender before using StitchMaker.

### Q. Clicking in the Scene view does not place control points
**A.** Check the following:
1. Verify that **`Edit Mode`** is enabled (highlighted in blue).
2. Ensure you are clicking directly on the surface of the assigned mesh (clicking empty space will not place nodes).
3. Ensure the Scene view is not in 2D mode, and that you are not clicking the backface of a single-sided polygon.

### Q. Stitches also appear on overlapping laces or collars
**A.** Make sure **`Island Constrain`** is ON in the stitch settings. If they still appear, the overlapping part is probably connected to the part below in UV space (they form a single UV island). Separate their UVs in your DCC tool.

### Q. I want to select other GameObjects in the Scene, but StitchMaker keeps placing nodes
**A.** Once you have finished path editing, toggle **`Edit Mode`** OFF in the StitchMaker window. This restores standard Unity selection behavior.

### Q. How should I use the exported textures?
**A.** There are two primary workflows:
1. **Compositing in Image Editors (Recommended)**:  
   Open your original garment texture in Photoshop, Clip Studio Paint, or Affinity Photo. Place `_Stitch_Color.png` on top as a normal layer, and overlay `_Stitch_Normal.png` using a normal map blending mode or combine them into a unified normal texture.  
   *(Enabling "Transparent Outside Stitches" makes this workflow especially seamless.)* To bake in the hole and sink shadows, place `_Stitch_AO.png` (black + alpha when exported with transparency) on top as a normal layer.
2. **Assigning to Shader Detail Slots**:  
   If you are using shaders such as lilToon or Poiyomi, you can assign the exported stitch textures directly to "Main Color 2nd" and "Detail Normal" slots to blend them in real time without modifying original texture files. `_Stitch_AO.png` can go into an AO (Occlusion) slot or a multiply color slot.
