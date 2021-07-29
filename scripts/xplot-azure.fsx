#r "nuget: Farmer"
#r "nuget: FSharp.Text.Docker, 1.0.5"

open System
open System.IO
open FSharp.Text.Docker.Dockerfile
open Farmer
open Farmer.Builders

// Some quick app we want to run. In the real world, you'll pull this source when building
// the image, but here we will just embed it.
let fsharpAppSource =
    File.ReadAllLines "xplot-server.fsx"
    |> Seq.filter (fun l -> not (l.StartsWith "#r"))
    |> String.concat Environment.NewLine


/// Builds a dockerfile with our source embedded in it.
let buildDockerfile (source:string) =
    // Encode the source so we can embed it in our Dockerfile.
    let encodedSource = source |> System.Text.Encoding.UTF8.GetBytes |> Convert.ToBase64String
    let appName = "MyApp"
    let buildImage = "builder"
    [
        // First the build image, using the SDK.
        From ("mcr.microsoft.com/dotnet/sdk", Some "5.0.302", Some buildImage)
        Run (ShellCommand $"dotnet new console -lang F# -n {appName}")
        WorkDir appName
        for package in [ "XPlot.Plotly"; "Suave" ] do
            Run (ShellCommand $"dotnet add package {package}")
        Run (ShellCommand $"echo {encodedSource} | base64 -d > Program.fs")
        Run (ShellCommand "dotnet build -c Release -o app")
        // Then the final image, which copies the app from the build image.
        From ("mcr.microsoft.com/dotnet/runtime", Some "5.0.8", None)
        Expose [80us]
        Copy (Source.SingleSource $"/{appName}/app", "/app", Some(BuildStage.Name buildImage))
        Cmd (ShellCommand $"dotnet /app/{appName}.dll")
    ] |> buildDockerfile

// Create Container Registry
let myAcr =
    containerRegistry {
        name "farmerdemoacr"
        sku ContainerRegistry.Basic
        enable_admin_user
    }

let deploymentIdentity = createUserAssignedIdentity "deployment-identity"

let imageTag = "1.0.0"

// Build and push to that ACR from a deploymentScript
let buildImage (dockerfile:string) =
    // Encode dockerfile as base64
    let encodedDockerfile = dockerfile |> System.Text.Encoding.UTF8.GetBytes |> Convert.ToBase64String

    deploymentScript {
        name "build-image"
        env_vars [ "ACR_NAME", myAcr.Name.Value ]
        identity deploymentIdentity
        depends_on myAcr
        script_content (
            [
                "set -eux"
                $"echo {encodedDockerfile} | base64 -d > Dockerfile"
                $"az acr build --registry $ACR_NAME --image fsharpwebapp:{imageTag} ."
            ] |> String.concat " ; "
        )
    }

let buildImageDeploymentScript =
    buildDockerfile fsharpAppSource
    |> buildImage 

/// Deploy a container group after the image is built. It will reference the container registry to get credentials.
let xplotContainerGroup =
    containerGroup {
        name "xplot-azure"
        public_dns "farmerchart" [TCP, 80us]
        add_instances [
            containerInstance {
                name "xplot-service"
                image $"{myAcr.Name.Value}.azurecr.io/fsharpwebapp:{imageTag}"
                add_public_ports [ 80us ]
            }
        ]
        reference_registry_credentials [
            Arm.ContainerRegistry.registries.resourceId myAcr.Name
        ]
        depends_on buildImageDeploymentScript
    }

/// Include them in an ARM deployment template
let template =
    arm {
        location Location.WestUS
        add_resources [
            deploymentIdentity
            myAcr
            buildImageDeploymentScript
            xplotContainerGroup
        ]
    }

template |> Writer.quickWrite (Path.GetFileNameWithoutExtension __SOURCE_FILE__)
