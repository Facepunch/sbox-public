# Layout conformance fixture tools

Both fixture sets come from upstream repositories and are regenerated with these scripts (Python 3):

- `convert_flex_fixtures.py <upstream>/tests/generated ../FlexConformance` - converts Yoga's Chrome-generated gtest fixtures
  ([facebook/yoga](https://github.com/facebook/yoga/tree/v3.2.1), tag **v3.2.1**, MIT) into the `[TestClass]` files in `../FlexConformance/`.
- `pack_layout_fixtures.py <upstream>/tests/xml ../../Data/Layout block grid blockgrid gridflex` - packs Taffy's
  Chrome-generated XML fixtures (DioxusLabs/taffy revision
  [`ac2b86929d35b7e0f1d24919595b89b4ce89baa4`](https://github.com/DioxusLabs/taffy/tree/ac2b86929d35b7e0f1d24919595b89b4ce89baa4), MIT) into one `layout-<group>.xml` per group for
  `BlockGridFixtureTests`. The checked-in files match that revision byte-for-byte apart from their wrapper.
- `update_layout_signatures.py <results.trx> ../../Data/Layout/layout-known-failures.txt` - prints an updated
  known-failure baseline after a deliberate layout change. Run the block/grid conformance tests with a TRX logger, review every
  changed mismatch, and use the output to update the baseline; never refresh signatures blindly.

Local suite and file names are neutral; generated source comments and XML headers retain upstream attribution.
Flex fixture factories explicitly assign omitted source styles; production nodes always use CSS defaults.
`FixtureGeometry` compares raw results in the source fixture's rounded coordinate convention without mutating
nodes. This is test observation only, not an alternate production layout mode. Block/grid fixtures retain
rectangle and child-count assertions; resolved-track diagnostics are no longer collected in production.
Full MIT notices are comments in `Sandbox.Layout/Layout/Flex/FlexLayout.cs` and `Sandbox.Layout/Layout/Grid/TrackSizing.cs`;
the layout assembly embeds these sources so binary distributions also retain the notices.
Signatures hash only the comparison lines (LF-normalized, with a trailing blank line), not diagnostic paths,
suite names, or input/actual tree dumps. Renaming the suites and data files does not require refreshing them.
