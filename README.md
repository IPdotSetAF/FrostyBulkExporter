# FrostyBulkExporter

This is a plugin for FrostyEditor which enables bulk exporting of Game assets from Frostbite Engine Games.

This plugin currently supports exporting of the following assets:
- Mesh assets
   - FBX
   - OBJ
- Skinned Mesh assets
   - FBX
   - OBJ
- Texture assets
   - PNG
   - HDR
   - TGA
   - DDS
- Audio assets
   - WAV

** Mesh export settings includes all settings that are currently available in `MeshSetPlugin`. **

## How to:

There are two ways to bulk export assets:

1. Right clicking on the desired folder in the `DataExplorer` tab on the left side of the `FrostyEditor` and choosing `Bulk Export` option.

2. Opening the `Bulk Exporter` window from the `View` Menu.
   
   2.1. Right clicking the folders or assets from the right panel, and including them to the selection.

   2.2. Clicking the export button on top of the window.

   - You can also right click on the folders and assets on the left selected assets explorer tab and exclude the items from selection.
   - There are asset filtering options on the top of the window that you can use. 

- After these steps the export folder selection window will appear, choose where you want to export the assets.

- Then, the export settings window will appear that lets you choose the asset types to export and the formats and other settings for each asset type.

- Enjoy.
## TODO:
- SVG asset support