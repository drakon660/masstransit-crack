[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Position = 0)]
    [string]$Version,

    [Parameter()]
    [string]$Root = $PSScriptRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-PackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$PackageReference
    )

    if ($PackageReference.HasAttribute("Version")) {
        return $PackageReference.GetAttribute("Version")
    }

    $versionNode = $PackageReference.SelectSingleNode("Version")
    if ($null -ne $versionNode) {
        return $versionNode.InnerText
    }

    return $null
}

function Set-PackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument]$Document,

        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$PackageReference,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if ($PackageReference.HasAttribute("Version")) {
        $PackageReference.SetAttribute("Version", $Version)
        return
    }

    $versionNode = $PackageReference.SelectSingleNode("Version")
    if ($null -ne $versionNode) {
        $versionNode.InnerText = $Version
        return
    }

    $newVersionNode = $Document.CreateElement("Version")
    $newVersionNode.InnerText = $Version
    [void]$PackageReference.AppendChild($newVersionNode)
}

function Save-ProjectFile {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlDocument]$Document,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.OmitXmlDeclaration = $true

    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $Document.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

$projectFiles = Get-ChildItem -Path $Root -Recurse -Filter *.csproj |
    Sort-Object FullName

if ($projectFiles.Count -eq 0) {
    throw "No .csproj files were found under '$Root'."
}

$results = New-Object System.Collections.Generic.List[object]
$updatedProjects = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)

foreach ($projectFile in $projectFiles) {
    $document = [System.Xml.XmlDocument]::new()
    $document.PreserveWhitespace = $true
    $document.Load($projectFile.FullName)

    $packageReferences = @(
        $document.SelectNodes("/Project/ItemGroup/PackageReference") |
            Where-Object { $_.Include -like "MassTransit*" }
    )

    if ($packageReferences.Count -eq 0) {
        continue
    }

    $projectChanged = $false

    foreach ($packageReference in $packageReferences) {
        $currentVersion = Get-PackageVersion -PackageReference $packageReference

        if ($PSBoundParameters.ContainsKey("Version") -and $currentVersion -ne $Version) {
            if ($PSCmdlet.ShouldProcess($projectFile.FullName, "Set $($packageReference.Include) version to $Version")) {
                Set-PackageVersion -Document $document -PackageReference $packageReference -Version $Version
                $projectChanged = $true
            }
        }

        $results.Add([pscustomobject]@{
            Project = $projectFile.FullName
            Package = $packageReference.Include
            Version = if ($PSBoundParameters.ContainsKey("Version")) { $Version } else { $currentVersion }
        })
    }

    if ($projectChanged) {
        Save-ProjectFile -Document $document -Path $projectFile.FullName
        [void]$updatedProjects.Add($projectFile.FullName)
    }
}

if ($results.Count -eq 0) {
    throw "No MassTransit package references were found under '$Root'."
}

if ($PSBoundParameters.ContainsKey("Version")) {
    if ($updatedProjects.Count -eq 0) {
        Write-Host "MassTransit packages are already set to version $Version."
    }
    else {
        Write-Host "Updated MassTransit packages to version $Version in $($updatedProjects.Count) project(s)."
    }
}

$results |
    Sort-Object Project, Package |
    Format-Table -AutoSize
