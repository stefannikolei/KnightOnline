#!/bin/bash

# Knight Online C# Servers Build Script

echo "Knight Online C# Servers Build Script"
echo "====================================="

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    print_error ".NET SDK is not installed. Please install .NET 8.0 SDK."
    exit 1
fi

# Get .NET version
DOTNET_VERSION=$(dotnet --version)
print_status "Using .NET SDK version: $DOTNET_VERSION"

# Clean previous builds
print_status "Cleaning previous builds..."
dotnet clean --verbosity quiet

# Restore packages
print_status "Restoring NuGet packages..."
if ! dotnet restore --verbosity quiet; then
    print_error "Failed to restore packages"
    exit 1
fi

# Build the solution
print_status "Building solution..."
if ! dotnet build --configuration Release --no-restore --verbosity quiet; then
    print_error "Build failed"
    exit 1
fi

print_status "Build completed successfully!"

# List built executables
print_status "Built servers:"
for dir in KnightOnline.*/; do
    if [ -d "$dir" ]; then
        SERVER_NAME=$(basename "$dir")
        EXE_PATH="$dir/bin/Release/net8.0/$SERVER_NAME"
        if [ -f "$EXE_PATH.dll" ]; then
            echo "  - $SERVER_NAME"
        fi
    fi
done

echo ""
print_status "To run a server:"
echo "  cd KnightOnline.<ServerName>"
echo "  dotnet run --configuration Release"
echo ""
print_status "Or run the built executable directly:"
echo "  dotnet KnightOnline.<ServerName>/bin/Release/net8.0/KnightOnline.<ServerName>.dll"