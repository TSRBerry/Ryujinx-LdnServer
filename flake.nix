{
  description = "Ryujinx-LdnServer UDP flake env";

  inputs = {
    flake-utils.url = "github:numtide/flake-utils";
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-23.11";
  };

  outputs = { self, nixpkgs, flake-utils }:
    let
      ldn_overlay = final: prev: {
        ryujinx-ldn-udp = with final;
          buildDotnetModule rec {
            pname = "ryujinx-ldn-udp-server";
            version = "1.0.0";

            src = self;

            projectFile = "LanPlayServer.sln";
            nugetDeps = ./deps.nix;

            dotnet-sdk = dotnetCorePackages.sdk_8_0;
            dotnet-runtime = dotnetCorePackages.runtime_8_0;
            selfContainedBuild = false;

            dotnetFlags = [
              "-p:PublishAOT=false"
              "-p:ExtraDefineConstants=DISABLE_CLI"
            ];

            executables = [ "LanPlayServer" ];
          };
      };
    in flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs {
          inherit system;
          overlays = [
            self.overlays."${system}"
          ];
        };
      in {
        packages = {
          default = self.packages.${system}.ryujinx-ldn-udp;
          ryujinx-ldn-udp = pkgs.ryujinx-ldn-udp;
        };

        overlays = ldn_overlay;

        # TODO: fully define the module
        nixosModules.ryujinx-ldn-udp = { pkgs, lib, config, ... }: {
          options = let inherit (lib) mkEnableOption mkOption types;
          in {
            services.ryujinx-ldn-udp = {
              enable = mkEnableOption (lib.mdDoc "Ryujinx LDN UDP Server");

              ldnHost = mkOption {
                type = types.str;
                default = "0.0.0.0";
                description = lib.mdDoc ''
                  The address which the LDN server uses.
                '';
              };

              ldnPort = mkOption {
                type = types.number;
                default = 30567;
                description = lib.mdDoc ''
                  The port which the LDN server exposes over ldnHost.
                '';
              };

              collectCrashDump = mkEnableOption (lib.mdDoc "Collect dotnet dumps on crash.");

              user = mkOption {
                type = types.str;
                default = "ryujinx-ldn";
                description =
                  lib.mdDoc "User account under which Ryujinx LDN runs.";
              };

              group = mkOption {
                type = types.str;
                default = "ryujinx-ldn";
                description = lib.mdDoc "Group under which Ryujinx LDN runs.";
              };
            };

          };

          config = let
            inherit (lib) mkIf;
            cfg = config.services.ryujinx-ldn-udp;
          in mkIf cfg.enable {
            nixpkgs.overlays = [ self.overlays."${system}" ];

            networking.nat.enable = true;

            systemd.services.ryujinx-ldn-udp = let ldn = pkgs.ryujinx-ldn-udp;
            in {
              description = "Ryujinx LDN UDP Server";
              after = [ "network.target" ];
              wantedBy = [ "multi-user.target" ];

              environment = {
                LDN_HOST = cfg.ldnHost;
                LDN_PORT = toString cfg.ldnPort;
              } // (if cfg.collectCrashDump then {
                DOTNET_DbgEnableMiniDump = "1";
                # Create a full dump
                DOTNET_DbgMiniDumpType = "4";
                DOTNET_DbgMiniDumpName = "/tmp/%e-%p_%t.coredump";
                DOTNET_CreateDumpDiagnostics = "1";
                DOTNET_EnableCrashReport = "1";
                DOTNET_CreateDumpVerboseDiagnostics = "1";
              } else {});

              serviceConfig = rec {
                Type = "simple";
                ExecStart = "${ldn}/bin/LanPlayServer";
                User = cfg.user;
                Group = cfg.group;
                WorkingDirectory = "${ldn}/lib/${ldn.pname}";
                Restart = "on-failure";
              };
            };

            users =
              mkIf (cfg.user == "ryujinx-ldn" && cfg.group == "ryujinx-ldn") {
                users.ryujinx-ldn = {
                  group = cfg.group;
                  isSystemUser = true;
                };
                extraUsers.ryujinx-ldn.uid = 992;

                groups.ryujinx-ldn = { };
                extraGroups.ryujinx-ldn = {
                  name = cfg.group;
                  gid = 990;
                };
              };

            networking.firewall.allowedUDPPorts = [ cfg.ldnPort ];

          };
        };

        checks = {
          vmTest = with import (nixpkgs + "/nixos/lib/testing-python.nix") {
            inherit system;
          };
            makeTest {
              name = "ryujinx-ldn-udp nixos module testing ${system}";

              nodes = {
                client = { ... }: {
                  imports = [ self.nixosModules.${system}.ryujinx-ldn-udp ];

                  services.ryujinx-ldn-udp.enable = true;
                };
              };

              testScript = ''
                start_all()
                client.wait_for_unit("ryujinx-ldn-udp.service")
              '';
            };

        };

        formatter = pkgs.nixfmt;

      });
}
