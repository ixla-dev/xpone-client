$script_root = $PSSCriptRoot;

$generator_path = resolve-path $script_root\tools\oag.exe;
$generator_output_path = "$script_root\src\Aida.Api.Client" 

if ([system.io.directory]::Exists($generator_output_path))
{
  del -recurse -force $generator_output_path
}

& $generator_path generate `
--input $script_root\aida-api.json `
--output $generator_output_path `
--project-name Aida.Api.Client `
--namespace Aida.Api.Client `
--lang csharp
