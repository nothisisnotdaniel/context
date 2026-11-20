# Dependencies

Every package and tool that Context uses or ships, with its version, its licence (SPDX expression) and a link to
the licence text of that version. `Use` says where it ends up: `runtime` ships with a release, `build` is only
needed to build or package it, `test` only runs in `dotnet test`. The full licence texts of the packages bundled
into `editor.js` and `mermaid-frame.js` are in `src/Context.Desktop/editor/THIRD-PARTY-LICENSES.txt`.

When a package changes (a `.csproj`, a `packages.lock.json`, `package.json` or `THIRD-PARTY-LICENSES.txt`), update
this file in the same commit: `DependencyListTests` checks that it matches them.

## NuGet packages

Direct and transitive packages from every `packages.lock.json` (all targets and runtime sections). A package in a
`src/` lock file is `runtime` (or `build` for an analyzer); one only in `tests/` lock files is `test`.

| Package | Version | Licence | Use | Licence text |
|---------|---------|---------|-----|--------------|
| Acornima | 1.7.0 | BSD-3-Clause | test | [nuget.org](https://www.nuget.org/packages/Acornima/1.7.0/License) |
| AngleSharp | 1.8.6 | MIT | test | [nuget.org](https://www.nuget.org/packages/AngleSharp/1.8.6/License) |
| AngleSharp.Css | 1.2.0 | MIT | test | [nuget.org](https://www.nuget.org/packages/AngleSharp.Css/1.2.0/License) |
| AngleSharp.Diffing | 1.1.1 | MIT | test | [nuget.org](https://www.nuget.org/packages/AngleSharp.Diffing/1.1.1/License) |
| bunit | 2.13.3 | MIT | test | [nuget.org](https://www.nuget.org/packages/bunit/2.13.3/License) |
| Jint | 4.17.0 | BSD-2-Clause | test | [nuget.org](https://www.nuget.org/packages/Jint/4.17.0/License) |
| Microsoft.AspNetCore.Authorization | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Authorization/9.0.1/License) |
| Microsoft.AspNetCore.Authorization | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Authorization/10.0.13/License) |
| Microsoft.AspNetCore.Components | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components/9.0.1/License) |
| Microsoft.AspNetCore.Components | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components/10.0.13/License) |
| Microsoft.AspNetCore.Components.Analyzers | 9.0.1 | MIT | build | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Analyzers/9.0.1/License) |
| Microsoft.AspNetCore.Components.Analyzers | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Analyzers/10.0.13/License) |
| Microsoft.AspNetCore.Components.Authorization | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Authorization/10.0.13/License) |
| Microsoft.AspNetCore.Components.Forms | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Forms/9.0.1/License) |
| Microsoft.AspNetCore.Components.Forms | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Forms/10.0.13/License) |
| Microsoft.AspNetCore.Components.Web | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Web/9.0.1/License) |
| Microsoft.AspNetCore.Components.Web | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Web/10.0.13/License) |
| Microsoft.AspNetCore.Components.WebAssembly | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.WebAssembly/10.0.13/License) |
| Microsoft.AspNetCore.Components.WebAssembly.Authentication | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.WebAssembly.Authentication/10.0.13/License) |
| Microsoft.AspNetCore.Components.WebView | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Components.WebView/9.0.1/License) |
| Microsoft.AspNetCore.Metadata | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Metadata/9.0.1/License) |
| Microsoft.AspNetCore.Metadata | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.AspNetCore.Metadata/10.0.13/License) |
| Microsoft.CodeCoverage | 17.14.1 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.CodeCoverage/17.14.1/License) |
| Microsoft.Data.Sqlite | 10.0.14 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.14/License) |
| Microsoft.Data.Sqlite.Core | 10.0.14 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Data.Sqlite.Core/10.0.14/License) |
| Microsoft.Extensions.Caching.Abstractions | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Abstractions/10.0.13/License) |
| Microsoft.Extensions.Caching.Memory | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory/10.0.13/License) |
| Microsoft.Extensions.Configuration | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration/9.0.1/License) |
| Microsoft.Extensions.Configuration | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration/10.0.13/License) |
| Microsoft.Extensions.Configuration.Abstractions | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Abstractions/9.0.1/License) |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Abstractions/10.0.13/License) |
| Microsoft.Extensions.Configuration.Binder | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Binder/9.0.1/License) |
| Microsoft.Extensions.Configuration.Binder | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Binder/10.0.13/License) |
| Microsoft.Extensions.Configuration.FileExtensions | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.FileExtensions/9.0.1/License) |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.FileExtensions/10.0.13/License) |
| Microsoft.Extensions.Configuration.Json | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Json/9.0.1/License) |
| Microsoft.Extensions.Configuration.Json | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Json/10.0.13/License) |
| Microsoft.Extensions.DependencyInjection | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/9.0.1/License) |
| Microsoft.Extensions.DependencyInjection | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.13/License) |
| Microsoft.Extensions.DependencyInjection.Abstractions | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/9.0.1/License) |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/10.0.13/License) |
| Microsoft.Extensions.Diagnostics | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics/10.0.13/License) |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions/10.0.13/License) |
| Microsoft.Extensions.FileProviders.Abstractions | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Abstractions/9.0.1/License) |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Abstractions/10.0.13/License) |
| Microsoft.Extensions.FileProviders.Composite | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Composite/9.0.1/License) |
| Microsoft.Extensions.FileProviders.Embedded | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Embedded/9.0.1/License) |
| Microsoft.Extensions.FileProviders.Physical | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Physical/9.0.1/License) |
| Microsoft.Extensions.FileProviders.Physical | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Physical/10.0.13/License) |
| Microsoft.Extensions.FileSystemGlobbing | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.FileSystemGlobbing/9.0.1/License) |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.FileSystemGlobbing/10.0.13/License) |
| Microsoft.Extensions.Localization.Abstractions | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Localization.Abstractions/10.0.13/License) |
| Microsoft.Extensions.Logging | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Logging/9.0.1/License) |
| Microsoft.Extensions.Logging | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Logging/10.0.13/License) |
| Microsoft.Extensions.Logging.Abstractions | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/9.0.1/License) |
| Microsoft.Extensions.Logging.Abstractions | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.13/License) |
| Microsoft.Extensions.Logging.Configuration | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Configuration/9.0.1/License) |
| Microsoft.Extensions.Logging.Console | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Console/9.0.1/License) |
| Microsoft.Extensions.Options | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Options/9.0.1/License) |
| Microsoft.Extensions.Options | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Options/10.0.13/License) |
| Microsoft.Extensions.Options.ConfigurationExtensions | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Options.ConfigurationExtensions/9.0.1/License) |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Options.ConfigurationExtensions/10.0.13/License) |
| Microsoft.Extensions.Primitives | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Primitives/9.0.1/License) |
| Microsoft.Extensions.Primitives | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Primitives/10.0.13/License) |
| Microsoft.Extensions.Validation | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.Extensions.Validation/10.0.13/License) |
| Microsoft.JSInterop | 9.0.1 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Microsoft.JSInterop/9.0.1/License) |
| Microsoft.JSInterop | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.JSInterop/10.0.13/License) |
| Microsoft.JSInterop.WebAssembly | 10.0.13 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.JSInterop.WebAssembly/10.0.13/License) |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1/License) |
| Microsoft.TestPlatform.ObjectModel | 17.14.1 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/17.14.1/License) |
| Microsoft.TestPlatform.TestHost | 17.14.1 | MIT | test | [nuget.org](https://www.nuget.org/packages/Microsoft.TestPlatform.TestHost/17.14.1/License) |
| Newtonsoft.Json | 13.0.3 | MIT | test | [nuget.org](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3/License) |
| Photino.Blazor | 4.0.13 | Apache-2.0 | runtime | [nuget.org](https://www.nuget.org/packages/Photino.Blazor/4.0.13/License) |
| Photino.Native | 4.0.22 | Apache-2.0 | runtime | [nuget.org](https://www.nuget.org/packages/Photino.Native/4.0.22/License) |
| Photino.NET | 4.0.16 | Apache-2.0 | runtime | [nuget.org](https://www.nuget.org/packages/Photino.NET/4.0.16/License) |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.13 | Apache-2.0 | runtime | [nuget.org](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.13/License) |
| SQLitePCLRaw.core | 2.1.13 | Apache-2.0 | runtime | [nuget.org](https://www.nuget.org/packages/SQLitePCLRaw.core/2.1.13/License) |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.13 | Apache-2.0 | runtime | [nuget.org](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.13/License) |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.13 | Apache-2.0 | runtime | [nuget.org](https://www.nuget.org/packages/SQLitePCLRaw.provider.e_sqlite3/2.1.13/License) |
| Velopack | 1.2.161 | MIT | runtime | [nuget.org](https://www.nuget.org/packages/Velopack/1.2.161/License) |
| xunit | 2.9.3 | Apache-2.0 | test | [nuget.org](https://www.nuget.org/packages/xunit/2.9.3/License) |
| xunit.abstractions | 2.0.3 | Apache-2.0 | test | [nuget.org](https://www.nuget.org/packages/xunit.abstractions/2.0.3/License) |
| xunit.analyzers | 1.18.0 | Apache-2.0 | test | [nuget.org](https://www.nuget.org/packages/xunit.analyzers/1.18.0/License) |
| xunit.assert | 2.9.3 | Apache-2.0 | test | [nuget.org](https://www.nuget.org/packages/xunit.assert/2.9.3/License) |
| xunit.core | 2.9.3 | Apache-2.0 | test | [nuget.org](https://www.nuget.org/packages/xunit.core/2.9.3/License) |
| xunit.extensibility.core | 2.9.3 | Apache-2.0 | test | [nuget.org](https://www.nuget.org/packages/xunit.extensibility.core/2.9.3/License) |
| xunit.extensibility.execution | 2.9.3 | Apache-2.0 | test | [nuget.org](https://www.nuget.org/packages/xunit.extensibility.execution/2.9.3/License) |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | test | [nuget.org](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.4/License) |

## npm packages

The packages in `src/Context.Desktop/editor/package.json` and every package with code in the two bundles
(`wwwroot/editor/editor.js` and `wwwroot/mermaid/mermaid-frame.js`), nested copies included. The bundled ones are
`runtime`; `esbuild` only builds the bundles. The `@esbuild/*` platform binaries and other npm packages that are
neither in `package.json` nor bundled are not listed.

| Package | Version | Licence | Use | Licence text |
|---------|---------|---------|-----|--------------|
| @braintree/sanitize-url | 7.1.2 | MIT | runtime | [LICENSE](https://unpkg.com/@braintree/sanitize-url@7.1.2/LICENSE) |
| chevrotain | 13.2.0 | Apache-2.0 | runtime | [LICENSE.txt](https://unpkg.com/chevrotain@13.2.0/LICENSE.txt) |
| @chevrotain/cst-dts-gen | 13.2.0 | Apache-2.0 | runtime | [LICENSE.txt](https://unpkg.com/@chevrotain/cst-dts-gen@13.2.0/LICENSE.txt) |
| @chevrotain/gast | 13.2.0 | Apache-2.0 | runtime | [LICENSE.txt](https://unpkg.com/@chevrotain/gast@13.2.0/LICENSE.txt) |
| @chevrotain/regexp-to-ast | 13.2.0 | Apache-2.0 | runtime | [LICENSE.txt](https://unpkg.com/@chevrotain/regexp-to-ast@13.2.0/LICENSE.txt) |
| @chevrotain/utils | 13.2.0 | Apache-2.0 | runtime | [LICENSE.txt](https://unpkg.com/@chevrotain/utils@13.2.0/LICENSE.txt) |
| cose-base | 1.0.3 | MIT | runtime | [LICENSE](https://unpkg.com/cose-base@1.0.3/LICENSE) |
| cose-base | 2.2.0 | MIT | runtime | [LICENSE](https://unpkg.com/cose-base@2.2.0/LICENSE) |
| cytoscape | 3.35.0 | MIT | runtime | [LICENSE](https://unpkg.com/cytoscape@3.35.0/LICENSE) |
| cytoscape-cose-bilkent | 4.1.0 | MIT | runtime | [LICENSE](https://unpkg.com/cytoscape-cose-bilkent@4.1.0/LICENSE) |
| cytoscape-fcose | 2.2.0 | MIT | runtime | [LICENSE](https://unpkg.com/cytoscape-fcose@2.2.0/LICENSE) |
| d3 | 7.9.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3@7.9.2/LICENSE) |
| d3-array | 2.12.1 | BSD-3-Clause | runtime | [LICENSE](https://unpkg.com/d3-array@2.12.1/LICENSE) |
| d3-array | 3.2.5 | ISC | runtime | [LICENSE](https://unpkg.com/d3-array@3.2.5/LICENSE) |
| d3-axis | 3.0.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-axis@3.0.1/LICENSE) |
| d3-brush | 3.0.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-brush@3.0.1/LICENSE) |
| d3-chord | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-chord@3.0.2/LICENSE) |
| d3-color | 3.1.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-color@3.1.1/LICENSE) |
| d3-contour | 4.0.3 | ISC | runtime | [LICENSE](https://unpkg.com/d3-contour@4.0.3/LICENSE) |
| d3-delaunay | 6.0.6 | ISC | runtime | [LICENSE](https://unpkg.com/d3-delaunay@6.0.6/LICENSE) |
| d3-dispatch | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-dispatch@3.0.2/LICENSE) |
| d3-drag | 3.0.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-drag@3.0.1/LICENSE) |
| d3-dsv | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-dsv@3.0.2/LICENSE) |
| d3-ease | 3.0.2 | BSD-3-Clause | runtime | [LICENSE](https://unpkg.com/d3-ease@3.0.2/LICENSE) |
| d3-fetch | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-fetch@3.0.2/LICENSE) |
| d3-force | 3.0.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-force@3.0.1/LICENSE) |
| d3-format | 3.1.3 | ISC | runtime | [LICENSE](https://unpkg.com/d3-format@3.1.3/LICENSE) |
| d3-geo | 3.1.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-geo@3.1.2/LICENSE) |
| d3-hierarchy | 3.1.3 | ISC | runtime | [LICENSE](https://unpkg.com/d3-hierarchy@3.1.3/LICENSE) |
| d3-interpolate | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-interpolate@3.0.2/LICENSE) |
| d3-path | 1.0.9 | BSD-3-Clause | runtime | [LICENSE](https://unpkg.com/d3-path@1.0.9/LICENSE) |
| d3-path | 3.1.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-path@3.1.1/LICENSE) |
| d3-polygon | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-polygon@3.0.2/LICENSE) |
| d3-quadtree | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-quadtree@3.0.2/LICENSE) |
| d3-random | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-random@3.0.2/LICENSE) |
| d3-sankey | 0.12.3 | BSD-3-Clause | runtime | [LICENSE](https://unpkg.com/d3-sankey@0.12.3/LICENSE) |
| d3-scale | 4.0.3 | ISC | runtime | [LICENSE](https://unpkg.com/d3-scale@4.0.3/LICENSE) |
| d3-scale-chromatic | 3.1.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-scale-chromatic@3.1.1/LICENSE) |
| d3-selection | 3.0.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-selection@3.0.1/LICENSE) |
| d3-shape | 1.3.7 | BSD-3-Clause | runtime | [LICENSE](https://unpkg.com/d3-shape@1.3.7/LICENSE) |
| d3-shape | 3.2.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-shape@3.2.1/LICENSE) |
| d3-time | 3.1.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-time@3.1.1/LICENSE) |
| d3-time-format | 4.1.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-time-format@4.1.1/LICENSE) |
| d3-timer | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-timer@3.0.2/LICENSE) |
| d3-transition | 3.0.2 | ISC | runtime | [LICENSE](https://unpkg.com/d3-transition@3.0.2/LICENSE) |
| d3-zoom | 3.0.1 | ISC | runtime | [LICENSE](https://unpkg.com/d3-zoom@3.0.1/LICENSE) |
| dagre-d3-es | 7.0.14 | MIT | runtime | [LICENSE.md](https://unpkg.com/dagre-d3-es@7.0.14/LICENSE.md) |
| dayjs | 1.11.23 | MIT | runtime | [LICENSE](https://unpkg.com/dayjs@1.11.23/LICENSE) |
| dompurify | 3.4.18 | MPL-2.0 OR Apache-2.0 | runtime | [LICENSE](https://unpkg.com/dompurify@3.4.18/LICENSE) |
| elkjs | 0.12.1 | EPL-2.0 OR GPL-3.0-or-later | runtime | [LICENSE.md](https://unpkg.com/elkjs@0.12.1/LICENSE.md) |
| entities | 4.5.0 | BSD-2-Clause | runtime | [LICENSE](https://unpkg.com/entities@4.5.0/LICENSE) |
| es-toolkit | 1.53.2 | MIT | runtime | [LICENSE](https://unpkg.com/es-toolkit@1.53.2/LICENSE) |
| esbuild | 0.28.2 | MIT | build | [LICENSE.md](https://unpkg.com/esbuild@0.28.2/LICENSE.md) |
| highlight.js | 11.11.2 | BSD-3-Clause | runtime | [LICENSE](https://unpkg.com/highlight.js@11.11.2/LICENSE) |
| @iconify/utils | 3.1.7 | MIT | runtime | [license.txt](https://unpkg.com/@iconify/utils@3.1.7/license.txt) |
| katex | 0.18.10 | MIT | runtime | [LICENSE](https://unpkg.com/katex@0.18.10/LICENSE) |
| khroma | 2.1.0 | MIT | runtime | [license](https://unpkg.com/khroma@2.1.0/license) |
| layout-base | 1.0.2 | MIT | runtime | [LICENSE](https://unpkg.com/layout-base@1.0.2/LICENSE) |
| layout-base | 2.0.1 | MIT | runtime | [LICENSE](https://unpkg.com/layout-base@2.0.1/LICENSE) |
| linkify-it | 5.0.2 | MIT | runtime | [LICENSE](https://unpkg.com/linkify-it@5.0.2/LICENSE) |
| lodash-es | 4.18.1 | MIT | runtime | [LICENSE](https://unpkg.com/lodash-es@4.18.1/LICENSE) |
| lowlight | 3.3.0 | MIT | runtime | [license](https://unpkg.com/lowlight@3.3.0/license) |
| markdown-it | 14.3.3 | MIT | runtime | [LICENSE](https://unpkg.com/markdown-it@14.3.3/LICENSE) |
| marked | 16.4.2 | MIT | runtime | [LICENSE.md](https://unpkg.com/marked@16.4.2/LICENSE.md) |
| mdurl | 2.1.0 | MIT | runtime | [LICENSE](https://unpkg.com/mdurl@2.1.0/LICENSE) |
| mermaid | 12.3.0 | MIT | runtime | [LICENSE](https://unpkg.com/mermaid@12.3.0/LICENSE) |
| @mermaid-js/parser | 2.0.1 | MIT | runtime | [LICENSE](https://unpkg.com/@mermaid-js/parser@2.0.1/LICENSE) |
| orderedmap | 2.1.1 | MIT | runtime | [LICENSE](https://unpkg.com/orderedmap@2.1.1/LICENSE) |
| prosemirror-commands | 1.7.2 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-commands@1.7.2/LICENSE) |
| prosemirror-history | 1.5.1 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-history@1.5.1/LICENSE) |
| prosemirror-inputrules | 1.5.1 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-inputrules@1.5.1/LICENSE) |
| prosemirror-keymap | 1.2.3 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-keymap@1.2.3/LICENSE) |
| prosemirror-markdown | 1.13.9 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-markdown@1.13.9/LICENSE) |
| prosemirror-model | 1.25.12 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-model@1.25.12/LICENSE) |
| prosemirror-schema-list | 1.6.0 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-schema-list@1.6.0/LICENSE) |
| prosemirror-state | 1.4.4 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-state@1.4.4/LICENSE) |
| prosemirror-transform | 1.12.5 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-transform@1.12.5/LICENSE) |
| prosemirror-view | 1.42.9 | MIT | runtime | [LICENSE](https://unpkg.com/prosemirror-view@1.42.9/LICENSE) |
| punycode.js | 2.3.1 | MIT | runtime | [LICENSE-MIT.txt](https://unpkg.com/punycode.js@2.3.1/LICENSE-MIT.txt) |
| rope-sequence | 1.3.4 | MIT | runtime | [LICENSE](https://unpkg.com/rope-sequence@1.3.4/LICENSE) |
| roughjs | 4.6.6 | MIT | runtime | [LICENSE](https://unpkg.com/roughjs@4.6.6/LICENSE) |
| stylis | 4.4.0 | MIT | runtime | [LICENSE](https://unpkg.com/stylis@4.4.0/LICENSE) |
| ts-dedent | 2.3.0 | MIT | runtime | [LICENSE](https://unpkg.com/ts-dedent@2.3.0/LICENSE) |
| uc.micro | 2.1.0 | MIT | runtime | [LICENSE.txt](https://unpkg.com/uc.micro@2.1.0/LICENSE.txt) |
| @upsetjs/venn.js | 2.0.0 | MIT | runtime | [LICENSE](https://unpkg.com/@upsetjs/venn.js@2.0.0/LICENSE) |
| w3c-keyname | 2.2.8 | MIT | runtime | [LICENSE](https://unpkg.com/w3c-keyname@2.2.8/LICENSE) |

## Tools and platform

The release publishes the desktop app `--self-contained`, so the .NET runtime ships inside it. The web view
(WebView2 on Windows, WKWebView on macOS) and `codesign` are part of the operating system and are not shipped.

| Package | Version | Licence | Use | Licence text |
|---------|---------|---------|-----|--------------|
| .NET runtime | 10.0.x | MIT | runtime | [LICENSE.TXT](https://github.com/dotnet/runtime/blob/release/10.0/LICENSE.TXT) |
| .NET SDK | 10.0.x | MIT | build | [LICENSE.TXT](https://github.com/dotnet/sdk/blob/release/10.0.1xx/LICENSE.TXT) |
| vpk | 1.2.161 | MIT | build | [nuget.org](https://www.nuget.org/packages/vpk/1.2.161/License) |

## Vendored assets

`src/Context.Desktop/wwwroot/` holds no vendored third-party files. `commandbar/` is hand-written, and
`editor/editor.js` (with `editor.css`) and `mermaid/mermaid-frame.js` are built from the npm packages listed above.
