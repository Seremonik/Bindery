# Changelog

All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.0] - 2026-06-11

### Added

- `BindableObject` base class implementing `INotifyBindablePropertyChanged` and `IDisposable`.
- `[BindableObject]` and `[BindableProperty]` attributes.
- Roslyn source generator: property bag generation, change notification wiring,
  `On<Name>Changed` partial callbacks, R3/UniRx auto-detection,
  diagnostics BG0001 to BG0004.
- `FormatBinding`: one-way source-to-string binding with `string.Format` support.
- `MultiBinding`: multiple sources through one format string.
- `ClickBinding`: Button to ICommand/Action/method binding with CanExecute support (R3).
- Binding Demo sample.
