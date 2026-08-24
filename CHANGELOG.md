# Changelog

## [2.1.2](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v2.1.1...v2.1.2) (2026-08-24)


### Bug Fixes

* **ci:** shorten server.json description to the registry's 100-char cap ([#175](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/175)) ([6565e65](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/6565e65edfd9ea90bf0b3c648ae48443ffc3a891))

## [2.1.1](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v2.0.0...v2.1.1) (2026-08-23)


### Features

* stop claiming dotMemory, shrink the image, and make generations real (part 2) ([#169](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/169)) ([494519b](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/494519b3eac1981343d389ce459f878ae7f331e7))


### Bug Fixes

* **ci:** publish to NuGet only from a release, and make main's prerelease label work ([#174](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/174)) ([42f9725](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/42f972504f94ecdb90f5759050acefff579fa0d3))
* stamp the real version into the packed server.json; delete the dead ProcessRunner ([#173](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/173)) ([eb44bb1](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/eb44bb1eaecc7036c5c2b675fa692e7bf9a18832))

## [2.0.0](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.7.2...v2.0.0) (2026-08-22)


### ⚠ BREAKING CHANGES

* the ensure_dotmemory MCP tool is removed. Six tools become five. Clients referencing it by name will get an unknown-tool error.

### Features

* collect heap data in-process, making analyze actually work (part 1) ([#163](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/163)) ([ba1864e](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/ba1864eb7a4a617533bd6d424a1adaf1597ebc93))


### Bug Fixes

* treat an empty snapshotPath as absent, not as a path ([#167](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/167)) ([67e0683](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/67e0683d9dc927717660d91eecc1ace8a6dc7a3b))

## [1.7.2](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.7.1...v1.7.2) (2026-08-21)


### Bug Fixes

* **ci:** opt in to the MTP runner so dotnet test works on .NET 10 SDK ([#150](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/150)) ([ae0e957](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/ae0e957c219268b52492bef149f4fa942c432174))

## [1.7.1](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.7.0...v1.7.1) (2026-07-28)


### Bug Fixes

* **ci:** retry MCP Registry publish, re-authenticating each attempt ([#121](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/121)) ([415357a](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/415357a78986abc7a87bc6d3b70ec9f45a4e47b6))

## [1.7.0](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.6.0...v1.7.0) (2026-07-28)


### Features

* Docker support and glama.json for Glama indexing ([#117](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/117)) ([8d79bd8](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/8d79bd8937b4d30574488db32c751bf2b0a04a5d))


### Bug Fixes

* make list_processes work, and restore execute bits on the extracted profiler ([#118](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/118)) ([dccd733](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/dccd733901a379fcb21e4fca16c0f5b677d31cbd))

## [1.6.0](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.5.2...v1.6.0) (2026-07-28)


### Features

* add .memorylens.json configuration loading with rule overrides ([35332db](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/35332db422cce0b640b7992233b4425072dc8542))
* add AnalysisEngine with analyze and get_rules MCP tools ([30005fe](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/30005feb09e662b43115bca52758c41caeec46c4))
* add DotMemoryAutoInstaller with platform mapping ([6e2c4fc](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/6e2c4fc48b683554d877d621e3219dfb9b18a561))
* add dotnet tool packaging to release workflow ([5f2de9f](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/5f2de9f88308133166e6618eaa7356df17e35589))
* add dotnet tool packaging to release workflow ([9cd5dea](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/9cd5dea2f952c0471043d4bc6facaba7d501d006))
* add ensure_dotmemory tool with tool manager ([fdb7857](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/fdb78577b42f39b36cce9f6fd5b681823246387c))
* add IDotMemoryAutoInstaller interface and fake ([203d61b](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/203d61b0d6e8bbe35ea47142269c20bf9813606c))
* add list_processes tool with process safety filter ([592add2](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/592add26534cd59cfba7c55a757cccc6025fd52e))
* add marketplace plugin packaging ([f637987](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/f637987c126fded45b5dec346a8f3eeba5985af9))
* add memorylens Claude skill ([6ac8804](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/6ac880491527995f130e53692a453bf0ec1eb8cb))
* add NuGet MCP server discovery metadata ([4cb99a0](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/4cb99a0b41d6ac45d99e8751f5bff1d98093d3d1))
* add NuGet MCP server discovery metadata ([99e75ca](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/99e75ca6c8e7f03228a72048da80703fa20c0955))
* add NuGet package icon ([9974885](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/99748853fd3f035ba63b92fffbd2f499e2c437bb))
* add NuGet publishing to release workflow ([940ce8c](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/940ce8c3d1af59038454dc504de6348059dfe109))
* add NuGet publishing to release workflow ([baf21ee](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/baf21eed33a54c5275fa7e3071cec607df4e6d49))
* add package icon for NuGet ([757917b](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/757917b109ad8835a4be01447a440a747d1aee13))
* add rule engine with 10 built-in memory analysis rules (ML001-ML010) ([9926116](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/9926116237df36bd4345e9d8763f9243296a187e))
* auto-download JetBrains dotMemory Console on first use ([4a6c7da](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/4a6c7dad6e48af7292fa1ecfc798391d1bd561e4))
* implement InstallLatestAsync with NuGet download and extraction ([72979a4](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/72979a4d0c5ab352a265e77744289bfadd877bf6))
* implement rule evaluation, add integration tests, fix all analyzer warnings ([92df746](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/92df746a8ee74bf3731e22ce1b5c4ede85d0778d))
* MemoryLens MCP server initial implementation ([814be64](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/814be646acd961d92a9c479d2419408e2366a38c))
* publish an npm launcher package for npx installs ([#115](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/115)) ([286046d](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/286046d9e9e26aaae41b97be120cbd252fc57647))
* README badges and MCP Registry prep ([5ddfddf](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/5ddfddf72d1f5067254181b85a4ac021b175d945))
* README badges, icon header, and MCP Registry prep ([482d8b3](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/482d8b342e31c98ead32aa004288a71d19f57f7c))
* register DotMemoryAutoInstaller in DI ([bc0155f](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/bc0155fa2b650f5ea6e19266a2b69a86565e5c9c))
* scaffold .NET MCP server project ([e4f6a94](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/e4f6a947409176140a2521026b93e2b0bb71e3bf))
* support official JetBrains dotMemory CLI via DOTMEMORY_PATH ([518e00d](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/518e00d8096df06677176169d341910e607c4913))
* switch to release-please for automated releases ([518d222](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/518d222504e1c3f58e40a693af7c79ab5c58203c))
* switch to release-please for automated releases ([012495b](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/012495bd48a522cf9f40930560fc15899cb5eb4b))
* wire IDotMemoryAutoInstaller into DotMemoryToolManager ([679f06d](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/679f06db90f2e6f7831fe2cc90c15d0bf84d9087))


### Bug Fixes

* add mcp-name to README for MCP registry ownership verification ([0ed495e](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/0ed495e0b2c35ec46816a329af412ae7bd0dd1d8))
* add mcp-name to README for MCP registry ownership verification ([f080957](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/f080957e6f9511024e97364b09af6f4b6664ea5c))
* add next-version to GitVersion config and fix CI build syntax ([36fc7f9](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/36fc7f9d0d760df23a09966580f451c91aa9ea46))
* cleanup on chmod failure, explicit DI wiring, retry on corrupt extraction ([925bf76](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/925bf761c2d73bbd5687806b1fde8bcdb5a719d9))
* correct dotnet-version format (10.0.x not net10.0) ([aaba7da](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/aaba7da083e151fcf98b80d13e3d0d61101aae66))
* correct MCP registry name casing and shorten description ([5e280d2](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/5e280d2aa37eb81ab39a9f692f426060cce41666))
* correct MCP registry name casing and shorten description ([fc38a35](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/fc38a3556135f344c0577c044b26d429725d0210))
* fall back to --help for dotMemory CLI version probe ([3291d58](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/3291d58aa1439342059c8ebf3538f3741ea15dac))
* fall back to --help for dotMemory CLI version probe ([89d068a](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/89d068a8deaa2cd4d36c2907bb26cf2b5dfa6f61)), closes [#44](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/44)
* include server.json in release-please version bumps ([fcca5d2](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/fcca5d2dc29ce43c40bfde05c28d5b6927ffb58f))
* include server.json in release-please version bumps ([3ea00fd](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/3ea00fdcfbf0dacd62d3b5a9b8447d6bfb1c28e6))
* isolate DOTMEMORY_PATH env var in EnsureDotMemory test ([82e6423](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/82e642319f0fcce3ff65a9be22a6bff061859e8b))
* remove Mainline strategy to work around GitVersion 6.6 bug ([98b9e32](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/98b9e3289e35b62209ab5c2e22dfd77fcfbe06df))
* set server.json version to match release-please manifest ([d62fae7](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/d62fae744b4b69c498dd788f4c0ce4a3d2c873ec))
* set server.json version to match release-please manifest ([644c9af](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/644c9af0b68f83b50408a85c4c89eb7c97d0966e))
* use FakeDotMemoryToolManager in EnsureDotMemory integration test ([33036d3](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/33036d37b148b97aca513eca5b646f9ceb97dc91))

## [1.5.1](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.5.0...v1.5.1) (2026-04-14)


### Bug Fixes

* fall back to --help for dotMemory CLI version probe ([3291d58](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/3291d58aa1439342059c8ebf3538f3741ea15dac))
* fall back to --help for dotMemory CLI version probe ([89d068a](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/89d068a8deaa2cd4d36c2907bb26cf2b5dfa6f61)), closes [#44](https://github.com/MarcelRoozekrans/memorylens-mcp/issues/44)

## [1.5.0](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.4.0...v1.5.0) (2026-04-14)


### Features

* add DotMemoryAutoInstaller with platform mapping ([6e2c4fc](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/6e2c4fc48b683554d877d621e3219dfb9b18a561))
* add IDotMemoryAutoInstaller interface and fake ([203d61b](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/203d61b0d6e8bbe35ea47142269c20bf9813606c))
* auto-download JetBrains dotMemory Console on first use ([4a6c7da](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/4a6c7dad6e48af7292fa1ecfc798391d1bd561e4))
* implement InstallLatestAsync with NuGet download and extraction ([72979a4](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/72979a4d0c5ab352a265e77744289bfadd877bf6))
* register DotMemoryAutoInstaller in DI ([bc0155f](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/bc0155fa2b650f5ea6e19266a2b69a86565e5c9c))
* wire IDotMemoryAutoInstaller into DotMemoryToolManager ([679f06d](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/679f06db90f2e6f7831fe2cc90c15d0bf84d9087))


### Bug Fixes

* cleanup on chmod failure, explicit DI wiring, retry on corrupt extraction ([925bf76](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/925bf761c2d73bbd5687806b1fde8bcdb5a719d9))

## [1.4.0](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.3.5...v1.4.0) (2026-04-14)


### Features

* support official JetBrains dotMemory CLI via DOTMEMORY_PATH ([518e00d](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/518e00d8096df06677176169d341910e607c4913))


### Bug Fixes

* isolate DOTMEMORY_PATH env var in EnsureDotMemory test ([82e6423](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/82e642319f0fcce3ff65a9be22a6bff061859e8b))
* use FakeDotMemoryToolManager in EnsureDotMemory integration test ([33036d3](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/33036d37b148b97aca513eca5b646f9ceb97dc91))

## [1.3.5](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.3.4...v1.3.5) (2026-03-31)


### Bug Fixes

* set server.json version to match release-please manifest ([d62fae7](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/d62fae744b4b69c498dd788f4c0ce4a3d2c873ec))
* set server.json version to match release-please manifest ([644c9af](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/644c9af0b68f83b50408a85c4c89eb7c97d0966e))

## [1.3.4](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.3.3...v1.3.4) (2026-03-29)


### Bug Fixes

* include server.json in release-please version bumps ([fcca5d2](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/fcca5d2dc29ce43c40bfde05c28d5b6927ffb58f))
* include server.json in release-please version bumps ([3ea00fd](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/3ea00fdcfbf0dacd62d3b5a9b8447d6bfb1c28e6))

## [1.3.3](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.3.2...v1.3.3) (2026-03-28)


### Bug Fixes

* add mcp-name to README for MCP registry ownership verification ([0ed495e](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/0ed495e0b2c35ec46816a329af412ae7bd0dd1d8))
* add mcp-name to README for MCP registry ownership verification ([f080957](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/f080957e6f9511024e97364b09af6f4b6664ea5c))

## [1.3.2](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.3.1...v1.3.2) (2026-03-28)


### Bug Fixes

* correct MCP registry name casing and shorten description ([5e280d2](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/5e280d2aa37eb81ab39a9f692f426060cce41666))
* correct MCP registry name casing and shorten description ([fc38a35](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/fc38a3556135f344c0577c044b26d429725d0210))

## [1.3.1](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.3.0...v1.3.1) (2026-03-22)


### Bug Fixes

* correct dotnet-version format (10.0.x not net10.0) ([aaba7da](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/aaba7da083e151fcf98b80d13e3d0d61101aae66))

## [1.3.0](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.2.0...v1.3.0) (2026-03-10)


### Features

* README badges and MCP Registry prep ([5ddfddf](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/5ddfddf72d1f5067254181b85a4ac021b175d945))

## [1.2.0](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.1.0...v1.2.0) (2026-03-10)


### Features

* add NuGet MCP server discovery metadata ([4cb99a0](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/4cb99a0b41d6ac45d99e8751f5bff1d98093d3d1))
* add NuGet MCP server discovery metadata ([99e75ca](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/99e75ca6c8e7f03228a72048da80703fa20c0955))

## [1.1.0](https://github.com/MarcelRoozekrans/memorylens-mcp/compare/v1.0.0...v1.1.0) (2026-03-10)


### Features

* add NuGet package icon ([9974885](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/99748853fd3f035ba63b92fffbd2f499e2c437bb))
* add package icon for NuGet ([757917b](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/757917b109ad8835a4be01447a440a747d1aee13))

## 1.0.0 (2026-03-09)


### Features

* add .memorylens.json configuration loading with rule overrides ([35332db](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/35332db422cce0b640b7992233b4425072dc8542))
* add AnalysisEngine with analyze and get_rules MCP tools ([30005fe](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/30005feb09e662b43115bca52758c41caeec46c4))
* add dotnet tool packaging to release workflow ([5f2de9f](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/5f2de9f88308133166e6618eaa7356df17e35589))
* add dotnet tool packaging to release workflow ([9cd5dea](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/9cd5dea2f952c0471043d4bc6facaba7d501d006))
* add ensure_dotmemory tool with tool manager ([fdb7857](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/fdb78577b42f39b36cce9f6fd5b681823246387c))
* add list_processes tool with process safety filter ([592add2](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/592add26534cd59cfba7c55a757cccc6025fd52e))
* add marketplace plugin packaging ([f637987](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/f637987c126fded45b5dec346a8f3eeba5985af9))
* add memorylens Claude skill ([6ac8804](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/6ac880491527995f130e53692a453bf0ec1eb8cb))
* add NuGet publishing to release workflow ([940ce8c](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/940ce8c3d1af59038454dc504de6348059dfe109))
* add NuGet publishing to release workflow ([baf21ee](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/baf21eed33a54c5275fa7e3071cec607df4e6d49))
* add rule engine with 10 built-in memory analysis rules (ML001-ML010) ([9926116](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/9926116237df36bd4345e9d8763f9243296a187e))
* implement rule evaluation, add integration tests, fix all analyzer warnings ([92df746](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/92df746a8ee74bf3731e22ce1b5c4ede85d0778d))
* MemoryLens MCP server initial implementation ([814be64](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/814be646acd961d92a9c479d2419408e2366a38c))
* scaffold .NET MCP server project ([e4f6a94](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/e4f6a947409176140a2521026b93e2b0bb71e3bf))
* switch to release-please for automated releases ([518d222](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/518d222504e1c3f58e40a693af7c79ab5c58203c))
* switch to release-please for automated releases ([012495b](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/012495bd48a522cf9f40930560fc15899cb5eb4b))


### Bug Fixes

* add next-version to GitVersion config and fix CI build syntax ([36fc7f9](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/36fc7f9d0d760df23a09966580f451c91aa9ea46))
* remove Mainline strategy to work around GitVersion 6.6 bug ([98b9e32](https://github.com/MarcelRoozekrans/memorylens-mcp/commit/98b9e3289e35b62209ab5c2e22dfd77fcfbe06df))
