# SVG Output

The simulator writes self-contained UTF-8 SVG with no JavaScript, external
assets, timestamps, or plotting-library dependency. All numeric attributes use
invariant-culture decimal points and stable element ordering.

## Per-simulation artifacts

Every `simulate` invocation writes exactly seven SVG files to each result
directory:

| File | Content |
|---|---|
| `true-surface.svg` | analytical ground-truth heatmap |
| `sampled-mesh.svg` | sparse grid lines, probe nodes, values, and extrema |
| `reconstructed-surface.svg` | interpolated heatmap |
| `signed-error.svg` | `estimated - true` diverging heatmap |
| `absolute-error.svg` | absolute-error sequential heatmap |
| `centerline-x.svg` | true and reconstructed X profile at the centre row |
| `centerline-y.svg` | true and reconstructed Y profile at the centre column |

`simulate --interpolation both` creates separate `bilinear` and `idw`
subdirectories, each with the complete artifact set.

## Comparison artifacts

`compare` creates one subdirectory for every mesh and algorithm, then writes:

- `comparison.csv` with one metrics row per mesh/algorithm;
- `mesh-comparison.svg` plotting RMSE and maximum absolute error against probe density.

The comparison renderer distinguishes algorithms by colour and metrics by solid
versus dashed paths.

## Document geometry

Generated SVG documents use a 760 x 620 view box. Heatmaps reserve a 500 x 500
plot beginning at `(80, 50)` with axis and legend space around it.

Physical coordinates map to SVG as:

$$x_{svg}=80+\frac{x-x_{min}}{x_{max}-x_{min}}500$$

$$y_{svg}=50+\frac{y_{max}-y}{y_{max}-y_{min}}500$$

The inverted Y expression keeps increasing physical Y upward while SVG Y
increases downward.

## Heatmaps

`SvgRenderer.RenderHeatmap` emits one rectangle for every evaluation cell, not
for every probe cell. A 251x251 evaluation therefore renders 250x250 heatmap
rectangles.

True and reconstructed surfaces use a common sequential range. Constant fields
use the midpoint colour instead of dividing by zero.

Signed error uses a blue-to-neutral-to-red diverging palette centred at zero.
The automatic range is symmetric:

$$[-E,+E], \qquad E=\max(|e_{min}|,|e_{max}|)$$

`--common-error-scale <mm>` replaces `E` with an explicit value, allowing plots
from separate runs to share the same visual scale. Absolute error uses `[0,E]`.

## Sampled mesh

The mesh view displays only information available to the interpolator:

- rectangular probed boundary;
- grid lines;
- one circle and signed height label per probe;
- sampled minimum and maximum values;
- physical X/Y axis labels in millimetres.

## Cross-sections

The centre row or column of the dense evaluation is rendered as two paths:

- blue solid path: analytical truth;
- red solid path: interpolated reconstruction.

Both axes are labelled in millimetres. Constant profiles receive a small
deterministic vertical padding so the chart remains valid.

## Determinism and validation

`SvgRenderer` and `MeshComparisonSvgRenderer` write UTF-8 without a byte-order
mark by using `UTF8Encoding(false)`. Tests verify:

- all seven expected file names;
- XML parsing and `<svg>` root;
- a `<title>` element and millimetre labels;
- absence of `NaN` and `Infinity`;
- byte-identical output for repeated requests;
- valid deterministic comparison SVG.

The CI smoke command also generates the full-resolution artifact set.
