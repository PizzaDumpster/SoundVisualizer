# Building SoundVisualizer

## Prerequisites
- .NET 8.0 SDK or later
- Windows OS (for running the application)
- For cross-platform builds on Linux/macOS, the project is configured with `EnableWindowsTargeting` property

## Building the Release

### Option 1: Build Only
```bash
dotnet restore SoundVisualizer.sln
dotnet build SoundVisualizer.sln --configuration Release
```

The compiled binaries will be located in:
`SoundVisualizer/bin/Release/net8.0-windows/`

### Option 2: Publish (Recommended)
```bash
dotnet publish SoundVisualizer.sln --configuration Release --output ./Release --runtime win-x64 --self-contained false
```

The complete release package will be in the `Release/` directory and will include:
- `SoundVisualizer.exe` - The main executable
- NAudio dependencies (NAudio.*.dll)
- Required runtime configuration files

## Running the Application
The application requires the .NET 8.0 Runtime to be installed on Windows.

To run the compiled release:
```bash
cd Release
SoundVisualizer.exe
```

## Note
This is a Windows Forms application that targets `net8.0-windows` and requires Windows to run.
