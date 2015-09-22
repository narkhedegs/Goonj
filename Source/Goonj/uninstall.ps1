param($installPath, $toolsPath, $package, $project)

$file = Join-Path $toolsPath 'Goonj.exe' | Get-ChildItem

$project.ProjectItems.Item($file.Name).Delete()