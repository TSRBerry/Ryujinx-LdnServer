{ pkgs ? import <nixpkgs> { }, stdenv ? pkgs.stdenv }:

let

in pkgs.buildDotnetModule rec {
  pname = "ryujinx-ldn-server";
  version = "0.1";

  src = ./.;

  projectFile = "LanPlayServer.sln";
  nugetDeps = ./deps.nix;

  dotnet-sdk = pkgs.dotnetCorePackages.sdk_8_0;
  dotnet-runtime = pkgs.dotnetCorePackages.runtime_8_0;
  selfContainedBuild = false;

  dotnetFlags = [
    "-p:ExtraDefineConstants=DISABLE_CLI"
  ];

  executables = [ "LanPlayServer" ];
}
