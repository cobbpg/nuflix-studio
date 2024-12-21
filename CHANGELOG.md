# Changelog

## 1.1 - 2024-12-21

The most important change since the previous version is that the exporter will always include all the necessary sprite moves to be able to display the full picture, no matter how many register updates the picture requires. Other than that, the editor was extended with some features requested by various people.

### Algorithm

* Fixed logic for including deferred updates and forced sprite moves.
* Allow removing underlay updates that are overridden in the next section.
* Spread out sprite moves over 8 sections in very busy pictures.
* Report rows with too many cycles in case of overflow.

### Editor

* Added support for customising key bindings in the editor.
* Changed zoom to lock the pixel to the mouse cursor if possible.
* Added ability to zoom with control + mouse wheel.
* Highlight the target pixel in the result image on the split view.
* Added rulers on the right side and the bottom.
* Always show the colour of the reference pixel under the cursor.
* Added separate primary and secondary colours for free drawing.
* Implemented colour picking when clicking with control pressed.

### Miscellaneous

* Images are magnified only by integer factors on the converter pane.
* Added button to open the manual.
* Updated screenshots in the manual and added example image credits.

## 1.0 - 2024-11-30

The first public version of NUFLIX Studio.