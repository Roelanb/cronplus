#!/bin/bash

# Development run script for Cronplus API
echo "Starting Cronplus API in development mode..."

# Set environment variables
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://localhost:5000"

# Clean and build
echo "Building the project..."
dotnet build

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

# Run the API
echo "Starting API on http://localhost:5000"
echo "Swagger UI available at: http://localhost:5000/swagger"
echo "Health check at: http://localhost:5000/health"
echo ""
echo "Press Ctrl+C to stop..."

dotnet run --no-build