///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument<string>("target", "Default");
var configuration = Argument<string>("configuration", "Release");

///////////////////////////////////////////////////////////////////////////////
// PREPARATION
///////////////////////////////////////////////////////////////////////////////

// Get whether or not this is a local build.
var local = BuildSystem.IsLocalBuild;
var isRunningOnAppVeyor = AppVeyor.IsRunningOnAppVeyor;
var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;

// Parse release notes.
var releaseNotes = ParseReleaseNotes("./ReleaseNotes.md");

// Get version.
var buildNumber = AppVeyor.Environment.Build.Number;
var version = releaseNotes.Version.ToString();
var semanticVersion = (local || target == "Publish") ? version : (version + string.Concat("-build-", buildNumber));

// Define directories.
var toolsDirectory = Directory("./Tools");
var sourceDirectory = Directory("./Source");
var testsDirectory = Directory("./Tests");
var outputDirectory = Directory("./Output");
var temporaryDirectory = Directory("./Temporary");
var testResultsDirectory = outputDirectory + Directory("TestResults");
var artifactsDirectory = outputDirectory + Directory("Artifacts");
var solutions = GetFiles("./**/*.sln");
var solutionPaths = solutions.Select(solution => solution.GetDirectory());

// Define files.
var nugetExecutable = "./Tools/nuget.exe"; 

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(() =>
{
    // Executed BEFORE the first task.
    Information("Target: " + target);
    Information("Configuration: " + configuration);
    Information("Is local build: " + local.ToString());
    Information("Is running on AppVeyor: " + isRunningOnAppVeyor.ToString());
    Information("Semantic Version: " + semanticVersion);
});

Teardown(() =>
{
    // Executed AFTER the last task.
    Information("Finished running tasks.");
});

///////////////////////////////////////////////////////////////////////////////
// TASK DEFINITIONS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    // Clean solution directories.
    foreach(var path in solutionPaths)
    {
        Information("Cleaning {0}", path);
        CleanDirectories(path + "/**/bin/" + configuration);
        CleanDirectories(path + "/**/obj/" + configuration);
    }

	CleanDirectories(outputDirectory);
});

Task("Create-Directories")
	.IsDependentOn("Clean")
    .Does(() =>
{
	var directories = new List<DirectoryPath>{ outputDirectory, testResultsDirectory, artifactsDirectory };
	directories.ForEach(directory => 
	{
		if (!DirectoryExists(directory))
		{
			CreateDirectory(directory);
		}
	});
});

Task("Restore-NuGet-Packages")
	.IsDependentOn("Create-Directories")
    .Does(() =>
{
    // Restore all NuGet packages.
    foreach(var solution in solutions)
    {
        Information("Restoring {0}...", solution);
        NuGetRestore(solution, new NuGetRestoreSettings { ConfigFile = solution.GetDirectory() + "/nuget.config" });
    }
});

Task("Patch-Assembly-Info")
    .IsDependentOn("Restore-NuGet-Packages")
	.WithCriteria(() => !local)
    .Does(() =>
{
	var assemblyInfoFiles = GetFiles("./**/AssemblyInfo.cs");
	foreach(var assemblyInfoFile in assemblyInfoFiles)
	{
	    CreateAssemblyInfo(assemblyInfoFile, new AssemblyInfoSettings {
			Version = version,
			FileVersion = version,
			InformationalVersion = semanticVersion,
			Copyright = "Copyright (c) Gaurav Narkhede"
		});
	}
});

Task("Build")
    .IsDependentOn("Patch-Assembly-Info")
    .Does(() =>
{
    // Build all solutions.
    foreach(var solution in solutions)
    {
        Information("Building {0}", solution);
        MSBuild(solution, settings => 
            settings.SetPlatformTarget(PlatformTarget.MSIL)
                .WithProperty("TreatWarningsAsErrors","true")
                .WithTarget("Build")
                .SetConfiguration(configuration));
    }
});

Task("Run-Unit-Tests")
    .IsDependentOn("Build")
    .Does(() =>
{
	var testAssemblies = GetFiles(testsDirectory.Path + "/**/*.Tests.dll");
	if(testAssemblies.Count() > 0)
	{
	    NUnit(testsDirectory.Path + "/**/*.Tests.dll", 
		new NUnitSettings 
			{ 
				OutputFile = testResultsDirectory.Path + "/TestResults.xml", 
				NoResults = true 
			}
		);
	}
});

Task("Create-NuGet-Packages")
    .IsDependentOn("Run-Unit-Tests")
    .Does(() =>
{
	var nuspecFiles = GetFiles(sourceDirectory.Path + "/**/*.nuspec");
	foreach(var nuspecFile in nuspecFiles)
	{
		var projectFileName = nuspecFile.GetFilenameWithoutExtension() + ".csproj";
		StartProcess(nugetExecutable, new ProcessSettings 
			{ 
				Arguments = "pack -Symbols -Tool " + projectFileName + " -Version " + semanticVersion + " -Properties Configuration=" + configuration, 
				WorkingDirectory = nuspecFile.GetDirectory() 
			}
		);
	}

	var nugetPackageFiles = GetFiles(sourceDirectory.Path + "/**/*.nupkg");
	MoveFiles(nugetPackageFiles, artifactsDirectory);
});

Task("Update-AppVeyor-Build-Number")
    .WithCriteria(() => isRunningOnAppVeyor)
    .Does(() =>
{
    AppVeyor.UpdateBuildVersion(semanticVersion);
});

Task("Upload-AppVeyor-Artifacts")
    .IsDependentOn("Create-NuGet-Packages")
    .WithCriteria(() => isRunningOnAppVeyor)
    .Does(() =>
{
	var artifacts = GetFiles(artifactsDirectory.Path + "/**/*.nupkg");
	foreach(var artifact in artifacts)
	{
		AppVeyor.UploadArtifact(artifact);
	}
});

Task("Publish-NuGet-Packages")
	.IsDependentOn("Upload-AppVeyor-Artifacts")
    .WithCriteria(() => !local)
    .WithCriteria(() => !isPullRequest)
    .Does(() =>
{
    // Resolve the API key.
    var apiKey = EnvironmentVariable("NuGetApiKey");
    if(string.IsNullOrEmpty(apiKey)) {
        throw new InvalidOperationException("Could not resolve NuGet API key.");
    }

	var nugetPackages = GetFiles(artifactsDirectory.Path + "/**/*.nupkg");
	foreach(var nugetPackage in nugetPackages)
	{
		NuGetPush(nugetPackage, new NuGetPushSettings {
			ApiKey = apiKey
		});
	}
});

///////////////////////////////////////////////////////////////////////////////
// TARGETS
///////////////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Run-Unit-Tests");

Task("Package")
	.IsDependentOn("Update-AppVeyor-Build-Number")
    .IsDependentOn("Upload-AppVeyor-Artifacts");
	
Task("Publish")
	.IsDependentOn("Update-AppVeyor-Build-Number")
    .IsDependentOn("Publish-NuGet-Packages");

///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);