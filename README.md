# Secure Copilot CLI Wrapper
This tool is designed to be a terminal wrapper around a secure containerised github copilot cli.

It is intended to check the prerequisites for running the container and the copilot cli inside.

The intended use is to be able to navigate to a path and run `spectre-copilot` to have a secure copilot cli available in that path.
It will mount the current path into the container under `/workspace` and run the copilot cli from there.

To limit the damage from the CLI, the application expects a PAT (Personal Access Token) to be passed as an environment 
variable to the container. It will not have access to any other files on the host machine, except for the mounted 
workspace. The PAT is set once and stored by the application under the user environment variable 
"GH_COPILOT_PAT" and will be passed to each new container instance. The application will also check for the presence of 
the PAT before running the container, and will prompt the user to set it if it is not found.

## Prerequisites
- Podman installed and accessible via the command line. The application uses the Podman API to manage containers, so it requires Podman to be installed and running on the host machine.
- GitHub Copilot CLI installed in the container
- A github PAT (Personal Access Token) with the necessary permissions to use the copilot cli, stored in the user environment variable "GH_COPILOT_PAT".
- Dotnet 10 to run the application.

## Usage
1. Start the application by running `spectre-copilot` in the terminal.
2. Follow the prompts to resolve any missing prerequisites, such as setting the "GH_COPILOT_PAT" environment variable if it is not found.
3. Select the container to start in the current directory. The application will mount the current directory into the container under `/workspace` and run the copilot cli from there.

## Design
The application is built using dotnet 10 using the Spectre.Console library for the CLI interface and the Docker.DotNet 
library to interact with Podman.

Testing is handled by the Spectre.Console.Cli.Testing using Xunit and FakeItEasy for mocking dependencies.

### Testing
The integration tests build the Podman images defined in the repo and run the container to check if the copilot cli is 
working as expected. These tests run in the local environment and require Podman to be running. 
They are not meant to be run in a CI/CD pipeline, but rather as a way to test the application locally. 
They also require that the environment variable "GH_COPILOT_PAT" is set, as they will not run without it.

## Podman images
This repo defines a series of Podman images that can be started by the application and are designed to limit what the
copilot cli can do and access. 

The default image is a dotnet 10 image with the copilot cli pre-installed, and a non-root user with limited permissions.

### Images
- `spectre-copilot-default`: The default image, based on dotnet 10 with the copilot cli pre-installed and a non-root user with limited permissions. Built from `Podman/dotnet10.Podman`

Build locally with standard Docker-compatible tooling: `docker build -f Podman/dotnet10.Podman -t spectre-copilot-default .`

Images are published to the GitHub Container Registry (GHCR) automatically on merge to `main` when files under `Podman/` or the publish workflow itself change.

## Future
- Create whitelist of allowed domains the copilot cli can access, and block all other network access from the container.
- Create new image to handle renpy development, with the copilot cli and renpy installed, and limited access to the rest of the system.
- Create tooling to setup ComfyUI and allow the copilot cli to interact with it, while still limiting its access to the rest of the system.
- Create tooling to setup local LLMs and allow the copilot cli to interact with them, while still limiting its access to the rest of the system.
