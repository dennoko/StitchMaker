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
   - [Mirror Function](#mirror-function)
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
- **Clear**: Detaches the assigned Renderer.
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
- **Save**: Saves all current paths, nodes, and export settings as an `.asset` file in your project. Useful for reusing stitch lines across outfits or creating backups.

#### Path List
- **Selecting a Path**: Click a path name in the list to select it for editing.
- **Eye Icon (Show / Hide)**: Toggles path visibility. Hidden paths are excluded from both the Scene view preview and texture export.
- **× Button**: Deletes the selected path.
- **+ Add Path**: Appends a new stitch path to the list. If a path is selected, the new path inherits its stitch settings (mask, scale, length, width, thread color, etc.), which is handy for placing the same stitch in several places. The shape (control points, Closed, Bezier Handles) and mirror settings are not inherited.

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
| **Groove Depth** | 0 – 2 | Steepness of the groove walls. The groove appears only in the Normal map and does not affect Color or Height (the fabric is at height 0 in the Height map, so nothing can go below it). |
| **Jitter: Position (mm)** | 0 – 2 mm | Adds random positional variation to each stitch for a hand-stitched, organic look. |
| **Jitter: Angle (°)** | 0 – 30° | Adds random angular variation to each stitch. |
| **Seed** | Integer | Random seed value for reproducible jitter generation. Changing this value produces a different jitter pattern. |

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

### PREVIEW / EXPORT

#### Real-time Preview
- **Preview Toggle**: Enables or disables the real-time stitch overlay in the Scene view.
- **Display Modes**:
  - **Combined**: Full shading preview showing colors, normals, and lighting combined.
  - **Normal**: Visualizes the tangent-space normal map.
  - **Height**: Visualizes the grayscale height map.

#### Export Settings
- **Resolution**: Output texture resolution (`512`, `1024`, `2048`, `4096`). Matching your garment's original texture resolution is recommended.
- **Padding (px)**: Dilates pixels beyond the decal border (default: `4 px`) to prevent dark seams and mipmap bleeding.
- **Export Checkboxes**:
  - **Color**: RGBA base color texture with alpha transparency.
  - **Normal**: Normal map texture (automatically imported as Texture Type `Normal map` in Unity).
  - **Height**: Grayscale height map (linear).
  - **Mask**: Grayscale coverage mask (white = stitch, black = background).
- **Overwrite**:
  - **ON**: Overwrites existing files with the same name.
  - **OFF**: Appends sequential numbers (`_1`, `_2`, etc.) to avoid overwriting existing textures.
- **Transparent Outside Stitches**:
  - **ON**: Normal and Height maps will also have transparency outside stitches (Color is always transparent). Perfect for layering on top of existing textures in image editing software. For paths with a groove, the groove remains visible in the Normal map even where there is no thread.
    - *Note: Unity's "Normal map" texture type does not treat alpha as transparency, so when this option is ON the Normal map is saved with Texture Type "Default" (sRGB OFF, Alpha Is Transparency ON). If you want to use it directly as a normal map in a shader inside Unity, change its Texture Type to "Normal map" in the import settings.*
  - **OFF**: Fills non-stitch background with neutral normal color (#8080FF) and height black (#000000).
- **Output Folder**: Destination folder inside your project (`Assets/...`). Click the `...` button to choose a folder via file dialog.
- **Export Textures**: Executes GPU baking and saves the PNG files to the specified folder. Once complete, Unity highlights the destination folder in the Project window.

#### Output Naming Convention
Textures are exported using the following format:
- `{RendererName}_Stitch_Color.png`
- `{RendererName}_Stitch_Normal.png`
- `{RendererName}_Stitch_Height.png`
- `{RendererName}_Stitch_Mask.png`

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

### Q. I want to select other GameObjects in the Scene, but StitchMaker keeps placing nodes
**A.** Once you have finished path editing, toggle **`Edit Mode`** OFF in the StitchMaker window. This restores standard Unity selection behavior.

### Q. How should I use the exported textures?
**A.** There are two primary workflows:
1. **Compositing in Image Editors (Recommended)**:  
   Open your original garment texture in Photoshop, Clip Studio Paint, or Affinity Photo. Place `_Stitch_Color.png` on top as a normal layer, and overlay `_Stitch_Normal.png` using a normal map blending mode or combine them into a unified normal texture.  
   *(Enabling "Transparent Outside Stitches" makes this workflow especially seamless.)*
2. **Assigning to Shader Detail Slots**:  
   If you are using shaders such as lilToon or Poiyomi, you can assign the exported stitch textures directly to "Main Color 2nd" and "Detail Normal" slots to blend them in real time without modifying original texture files.
