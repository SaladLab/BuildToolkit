@echo off

pushd %~dp0

REM .\tools\nuget\NuGet.exe update -self

IF NOT EXIST .\packages\_\FAKE (
  .\tools\nuget\NuGet.exe install FAKE -ConfigFile .\tools\nuget\NuGet.Config -OutputDirectory packages\_ -ExcludeVersion -Version 4.22.8
)

IF NOT EXIST .\packages\_\xunit.runner.console (
  .\tools\nuget\NuGet.exe install xunit.runner.console -ConfigFile .\tools\nuget\NuGet.Config -OutputDirectory packages\_ -ExcludeVersion -Version 2.1.0
)

IF NOT EXIST .\packages\_\OpenCover (
  .\tools\nuget\NuGet.exe install OpenCover -ConfigFile .\tools\nuget\NuGet.Config -OutputDirectory packages\_ -ExcludeVersion -Version 4.6.519
)

IF NOT EXIST .\packages\_\coveralls.io (
  .\tools\nuget\NuGet.exe install coveralls.io -ConfigFile .\tools\nuget\NuGet.Config -OutputDirectory packages\_ -ExcludeVersion -Version 1.3.4
)

set encoding=utf-8
packages\_\FAKE\tools\FAKE.exe build.fsx %*

popd
