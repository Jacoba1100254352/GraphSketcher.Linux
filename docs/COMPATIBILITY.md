# Compatibility

## Native documents

`.graphsketch` is the Windows port's lossless editable format. It is UTF-8 JSON
with an explicit format version. Files are validated and normalized when
opened.

## Original `.ograph` documents

The original app uses XML in the namespace
`http://www.omnigroup.com/namespace/OmniGraphSketcher/v1`. Desktop files are
usually ZIP archives containing `contents.xml` and `preview.pdf`; iPad and
autosave files may be plain XML.

The current importer reads both containers and supports:

| Original element | Import behavior |
| --- | --- |
| Canvas size/background | Preserved |
| X/Y axes | Range, scale, visibility, grid, spacing, and titles preserved |
| Vertices | Coordinates, color, size, shape, labels, and Y error values preserved where representable |
| Connect lines | Straight/curved mode, width, dash style, and vertex order preserved |
| Fit lines | Imported as ordinary series using stored endpoints |
| Free vertices | Grouped into sensible series by style |
| Text labels | Standalone text becomes an annotation; point-owned text becomes a point label |
| Fills | Boundary points imported; advanced fill geometry is not yet editable |
| Groups/snapping | Flattened; relationships are not yet round-tripped |
| Rich text runs | Imported as plain text |
| Preview PDF | Ignored |

Opening a legacy document never modifies it. Save as `.graphsketch` to retain
all features supported by this port.

## Export

SVG export is vector-based and includes axes, grids, series, error bars,
markers, annotations, titles, and legends. CSV export writes one long-form row
per data point. Full original `.ograph` writing is planned after preservation
of unsupported elements can be guaranteed.
