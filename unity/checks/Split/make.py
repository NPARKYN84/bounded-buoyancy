import os, sys
ver = sys.argv[1]
unity6 = ver == "6000_0"
defs = "UNITY_2022_3_OR_NEWER;UNITY_EDITOR" + (";UNITY_6000_0_OR_NEWER" if unity6 else "")
# The HDRP link is compiled as Unity does with the HDRP that comes with each version: HDRP 14 with Unity 2022.3, HDRP 17
# with Unity 6 (the asmdef's versionDefines set these).
hdrp_defs = ";BB_HDRP_WATER" + (";BB_HDRP_15_OR_NEWER;BB_HDRP_16_OR_NEWER;BB_STUB_HDRP17" if unity6 else "")
pkg = "../../../../com.parkyn.bounded-buoyancy"
projects = {
    "UnityEngineStub": (["../../../StubCompile/UnityStubs.cs", "../../../StubCompile/SampleStubs.cs", "../../../StubCompile/TestStubs.cs"], []),
    "HDRPStub": (["../../../StubCompile/HDRPStubs.cs"], ["UnityEngineStub"]),
    "Runtime": ([pkg + "/Runtime/**/*.cs"], ["UnityEngineStub"]),
    "HDRP": ([pkg + "/Runtime/HDRP/*.cs"], ["UnityEngineStub", "HDRPStub", "Runtime"]),
    "Tests": ([pkg + "/Tests/Runtime/*.cs"], ["UnityEngineStub", "Runtime"]),
    "EditorTests": ([pkg + "/Tests/Editor/*.cs"], ["UnityEngineStub", "Runtime"]),
    "Samples": ([pkg + "/Samples~/Demo/*.cs"], ["UnityEngineStub", "Runtime"]),
    "SamplesEditor": ([pkg + "/Samples~/Demo/Editor/*.cs"], ["UnityEngineStub", "Runtime", "Samples"]),
}
for name, (srcs, refs) in projects.items():
    d = os.path.join(ver, name)
    os.makedirs(d, exist_ok=True)
    warn = "" if name == "UnityEngineStub" else "<TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsAsErrors>CS0618;CS0612</WarningsAsErrors>"
    exclude = f' Exclude="{pkg}/Runtime/HDRP/**"' if name == "Runtime" else ""
    items = "\n".join(f'    <Compile Include="{s}"{exclude} />' for s in srcs)
    project_defs = defs + (hdrp_defs if name in ("HDRP", "HDRPStub") else "")
    prefs = "\n".join(f'    <ProjectReference Include="../{r}/{r}.csproj" />' for r in refs)
    open(os.path.join(d, name + ".csproj"), "w").write(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <DefineConstants>{project_defs}</DefineConstants>
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
