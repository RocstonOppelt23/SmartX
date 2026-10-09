# Smart-X IoT Mesh Ecosystem

PROG7312 / AAPD7112 — Advanced Application Development
Rocston John Oppelt | ST10276665 | IIE Varsity College (Emeris) Cape Town

Repository: https://github.com/RocstonOppelt23/SmartX.git

## Requirements

- .NET 10 SDK
- Visual Studio 2022 (or later) with ASP.NET and web development workload

## Restore dependencies

Clone the repository, then open SmartX.slnx in Visual Studio. Dependencies restore automatically on open, or manually with: dotnet restore

## Compile

Build the solution in Visual Studio (Ctrl+Shift+B), or: dotnet build

## Boot the API

Right-click the solution, Configure Startup Projects, Multiple startup projects, set SmartX.Api to Start. The API listens on http://localhost:5119 and https://localhost:7251. If running standalone from the API project folder: dotnet run

## Run the client

Set SmartX.Client to Start alongside the API (same Configure Startup Projects dialog), then press F5. A browser tab opens with the Blazor client running on https://localhost:7111. If the API's ports differ from the defaults above on your machine, update the BaseAddress in SmartX.Client/Program.cs to match before running.
