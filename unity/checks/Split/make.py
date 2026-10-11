import os, sys
ver = sys.argv[1]
defs = "UNITY_2022_3_OR_NEWER;UNITY_EDITOR" + (";UNITY_6000_0_OR_NEWER" if ver == "6000_0" else "")
pkg = "../../../../com.parkyn.bounded-buoyancy"
projects = {
    "UnityEngineStub": (["../../../StubCompile/UnityStubs.cs", "../../../StubCompile/SampleStubs.cs", "../../../StubCompile/TestStubs.cs"], []),
    "Runtime": ([pkg + "/Runtime/**/*.cs"], ["UnityEngineStub"]),
    "Tests": ([pkg + "/Tests/Runtime/*.cs"], ["UnityEngineStub", "Runtime"]),
    "EditorTests": ([pkg + "/Tests/Editor/*.cs"], ["UnityEngineStub", "Runtime"]),
    "Samples": ([pkg + "/Samples~/Demo/*.cs"], ["UnityEngineStub", "Runtime"]),
    "SamplesEditor": ([pkg + "/Samples~/Demo/Editor/*.cs"], ["UnityEngineStub", "Runtime", "Samples"]),
}
for name, (srcs, refs) in projects.items():
    d = os.path.join(ver, name)
    os.makedirs(d, exist_ok=True)
    warn = "" if name == "UnityEngineStub" else "<TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsAsErrors>CS0618;CS0612</WarningsAsErrors>"
    items = "\n".join(f'    <Compile Include="{s}" />' for s in srcs)
    prefs = "\n".join(f'    <ProjectReference Include="../{r}/{r}.csproj" />' for r in refs)
    open(os.path.join(d, name + ".csproj"), "w").write(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <DefineConstants>{defs}</DefineConstants>
    <AssemblyName>{name}_{ver}</AssemblyName>
    <NoWarn>CS0626;CS0824;CS0067;CS0649;CS0414;CS0169</NoWarn>
    {warn}
  </PropertyGroup>
  <ItemGroup>
{items}
  </ItemGroup>
  <ItemGroup>
{prefs}
  </ItemGroup>
</Project>
""")
