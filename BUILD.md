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

## Automated Releases

The repository includes a GitHub Actions workflow that creates releases when version tags are pushed or when triggered manually from the Actions tab.

### Creating a Release

#### Option 1: Using Git Tags (Automatic)

1. Tag your commit with a version number:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

2. The GitHub Actions workflow will automatically:
   - Build the application in Release configuration
   - Publish the Windows executable with dependencies
   - Create a ZIP archive of the release
   - Create a GitHub release with auto-generated release notes
   - Upload the ZIP file as a release asset

#### Option 2: Manual Trigger

1. Go to the "Actions" tab in the GitHub repository
2. Select the "Create Release" workflow
3. Click "Run workflow"
4. Enter the tag name (e.g., `v1.0.0`) when prompted
5. The workflow will build and create the release with the specified tag

The release will be available on the GitHub releases page with the filename:
`SoundVisualizer-win-x64.zip`

### Version Tag Format
Use semantic versioning with a `v` prefix:
- `v1.0.0` - Major release
- `v1.1.0` - Minor release
- `v1.0.1` - Patch release

## Running the Application
The application requires the .NET 8.0 Runtime to be installed on Windows.

To run the compiled release:
```bash
cd Release
SoundVisualizer.exe
```

## Note
This is a Windows Forms application that targets `net8.0-windows` and requires Windows to run.

