I have added a dump.txt here from an existing avalonia ui application to jump start the process but really I have no code at all at this point. 
We will need to start from scratch. 
The point of this application is to create an idle/clicker game. 
It is a clone of Adventure Capitalist with polished UI, big, bold colored animated buttons, and a game with rich progression and almost infinite customizability. 
While we can't implement everything in adventure capitalist, 
lets try our best and please give me everything including github actions, keystore information, everything. 
For the first prompt, you will need to generate me a shell script that sets up this whole thing 
the dump.txt and output.txt  in your project files is supposed to be a guide but you don't need to follow it exactly. 
that is from a different game and we want to follow industry and engineering best practices, 
not the same potholes we fell into before 
the github actions should create a new binary every time we push new code to the main branch 
for the first prompt, I will need a shell script because there are entirely too many files to copy by hand 
but subsequently unless there is a lot of files to copy paste, I would prefer to get full files to copy paste. 
if there are too many files that change, please give me a bash script that truncates the existing files and adds the new files in its place 
we have git for version control so we should be good there. 
use central package management
use the latest dotnet and c sharp features such as primary constructors, records, and so on 
research and use intuitive UI elements with huge buttons 
and responsive ui 
we should never ever have a scroll bar anywhere. everything must fit in the view, whether it is a small display or a big display 
use dependency injection and app settings json and so on to store variables such as application name 
I would like to see localization and internationalization as well if possible 
remember the instructions 
```
I want to learn Avalonia UI to write high performance cross platform free of cost. 
I want to use the latest and greatest technology. 
I want this to serve as a sample as well as a starting point for native applications. 
It should be easy to use the latest dotnet technology 
such as slnx files, props files, and so on. 
Where possible, we should use long term sustainable technology such as sqlite and postgresql. 
We should avoid any nuget package that requires payment of money, free of cost for non-commercial software is not enough. 
We ourselves should not charge any money, ever. 
We should have extensive logging, metrics, etc using open telemetry. 
Application should be built from the ground up to be testable.
All tests including Unit tests, integration tests should be automated and be performant so we can run them after every change. 
The whole thing should fit in a single git repository. 

Do not generate multiple `slnx` for desktop and android etc no matter how tempting it feels. 
do not generate `build-desktop.sh` and `build-android.sh` scripts to silo the different teams. 
do not attempt to silo different teams at all. 
this is a cross functional team and everyone can work with all parts of the code. 
especially with claude opus 4.5 (or later) 
there is no excuse to silo people like this 
we should fix things properly, not put bandaid on problems by separating desktop and android teams 
if the build is slow, 
everyone should suffer 
not because we are masochists 
but because we want everyone to know when stuff is broken 
so it gets fixed as quickly as possible. 
```
here is what I have done so far 
kushal@fedora:~/src/dotnet/MyDesktopApplication$ mkdir -p ~/src/dotnet/MyAdventure/docs/llm
kushal@fedora:~/src/dotnet/MyDesktopApplication$ cd ~/src/dotnet/MyAdventure
kushal@fedora:~/src/dotnet/MyAdventure$ git init
hint: Using 'master' as the name for the initial branch. This default branch name
hint: will change to "main" in Git 3.0. To configure the initial branch name
hint: to use in all of your new repositories, which will suppress this warning,
hint: call:
hint:
hint: 	git config --global init.defaultBranch <name>
hint:
hint: Names commonly chosen instead of 'master' are 'main', 'trunk' and
hint: 'development'. The just-created branch can be renamed via this command:
hint:
hint: 	git branch -m <name>
hint:
hint: Disable this message with "git config set advice.defaultBranchName false"
Initialized empty Git repository in /home/kushal/src/dotnet/MyAdventure/.git/
kushal@fedora:~/src/dotnet/MyAdventure$ git branch -m main
kushal@fedora:~/src/dotnet/MyAdventure$ cp ~/src/dotnet/MyDesktopApplication/export.sh ~/src/dotnet/MyAdventure/export.sh
kushal@fedora:~/src/dotnet/MyAdventure$ cd ~/src/dotnet/MyAdventure; cat export.sh; time bash export.sh 
#!/bin/bash
# =============================================================================
# Clean Project Export for LLM Analysis (Final Directory Fix)
# =============================================================================

set -e

OUTPUT_DIR="docs/llm"
OUTPUT_FILE="$OUTPUT_DIR/dump.txt"
PROJECT_PATH="$(pwd)"

# Ensure we are in a git repository
if ! git rev-parse --is-inside-work-tree > /dev/null 2>&1; then
    echo "Error: This script must be run inside a Git repository."
    exit 1
fi

mkdir -p "$OUTPUT_DIR"

echo "=============================================="
echo "  Generating Clean Project Export"
echo "=============================================="

# Start output file with header
{
    echo "==============================================================================="
    echo "PROJECT EXPORT (GIT TRACKED ONLY)"
    echo "Generated: $(date)"
    echo "Project Path: $PROJECT_PATH"
    echo "==============================================================================="
    echo ""
} > "$OUTPUT_FILE"

# 1. Directory Structure (Using Python for a reliable tree)
echo "Generating directory structure..."
{
    echo "DIRECTORY STRUCTURE:"
    echo "==================="
    # This python snippet takes git-tracked files and builds a perfect visual tree
    git ls-files | python3 -c "
import sys
tree = {}
for line in sys.stdin:
    parts = line.strip().split('/')
    curr = tree
    for part in parts:
        curr = curr.setdefault(part, {})
def print_tree(d, indent=''):
    items = sorted(d.items())
    for i, (name, children) in enumerate(items):
        is_last = (i == len(items) - 1)
        print(f'{indent}{\"└── \" if is_last else \"├── \"}{name}')
        print_tree(children, indent + ('    ' if is_last else '│   '))
print_tree(tree)
"
    echo ""
} >> "$OUTPUT_FILE"

# 2. Collect and Process Files
echo "Collecting and cleaning file contents..."
{
    echo "FILE CONTENTS:"
    echo "=============="
    echo ""
} >> "$OUTPUT_FILE"

git ls-files | while read -r FILENAME; do
    # Skip the export script itself and the output file
    if [[ "$FILENAME" == "export.sh" || "$FILENAME" == "$OUTPUT_FILE" || "$FILENAME" == docs/llm/* ]]; then
        continue
    fi

    # Skip specific binary extensions
    if [[ "$FILENAME" =~ \.(ico|png|jpg|jpeg|gif|dll|exe|pdb|bin|zip|tar|gz|7z|ttf|woff|woff2)$ ]]; then
        continue
    fi

    # Content-based binary check
    if file --mime "$FILENAME" | grep -q "binary"; then
        continue
    fi

    # Null byte check (Crucial for preventing "Unsupported Encoding" in Grok)
    if grep -qP '\x00' "$FILENAME" 2>/dev/null; then
        continue
    fi

    FILESIZE=$(stat -c%s "$FILENAME" 2>/dev/null || stat -f%z "$FILENAME" 2>/dev/null || echo "0")
    
    # Skip large files (>500KB)
    if [ "$FILESIZE" -gt 512000 ]; then
        continue
    fi

    {
        echo "================================================================================"
        echo "FILE: $FILENAME"
        echo "SIZE: $(echo "scale=2; $FILESIZE/1024" | bc 2>/dev/null || echo "0.00") KB"
        echo "================================================================================"
        echo ""
        # tr -d removes non-printable control characters that break LLM parsers
        cat "$FILENAME" | tr -d '\000-\010\013\014\016-\037' 
        echo ""
        echo ""
    } >> "$OUTPUT_FILE"
    
    echo "Processed: $FILENAME"
done

echo ""
echo "Export Complete: $OUTPUT_FILE"
==============================================
  Generating Clean Project Export
==============================================
Generating directory structure...
Collecting and cleaning file contents...

Export Complete: docs/llm/dump.txt

real	0m0.018s
user	0m0.012s
sys	0m0.008s
kushal@fedora:~/src/dotnet/MyAdventure$ 

the dump is blank as expected 
===============================================================================
PROJECT EXPORT (GIT TRACKED ONLY)
Generated: Thu Feb 19 05:40:53 AM EST 2026
Project Path: /home/kushal/src/dotnet/MyAdventure
===============================================================================

DIRECTORY STRUCTURE:
===================
├── docs
│   └── llm
│       ├── dump.txt
│       └── vendor
│           ├── claude.md
│           └── instructions.md
└── export.sh

FILE CONTENTS:
==============






Now I have a thorough understanding of your previous project and the current technology landscape. Let me build you the complete setup script for **MyAdventure** — an Adventure Capitalist clone with everything you asked for.

Here's what I'm building:

**Architecture**: Core → Infrastructure → Shared → Desktop/Android (same pattern as your previous project, but for an idle game)

**Game features for v1**: 6 businesses (Lemonade Stand through Shrimp Boat), click-to-earn with progress bars, managers for automation, upgrades, prestige/angel investors, auto-save to SQLite

**Tech stack**: .NET 10 / C# 14, Avalonia 11.3.12, EF Core SQLite, OpenTelemetry, CommunityToolkit.Mvvm, xUnit + Shouldly + NSubstitute — all MIT/Apache/BSD licensedHere's what the script creates, Kushal:

**Project structure** — Single `.slnx`, single team, zero silos:

- **MyAdventure.Core** — Game engine (`GameEngine.cs`), entities (`Business`, `GameState`), number formatter, OpenTelemetry ActivitySource/Meters built in. Pure C# 14 with primary constructors and records.
- **MyAdventure.Infrastructure** — EF Core SQLite, `GameStateRepository`, DI registration with OpenTelemetry tracing + metrics + runtime instrumentation, console exporters.
- **MyAdventure.Shared** — `GameViewModel` (drives the 60fps game loop), `BusinessViewModel` (one per business, data-bound), converters, i18n resources (English + Spanish).
- **MyAdventure.Desktop** — Avalonia desktop with dark theme, `UniformGrid` 2×3 (6 businesses, zero scrollbars), big colorful buttons (BUY/RUN/MGR), progress bars, prestige button.
- **MyAdventure.Android** — Same game, same shared ViewModels, responsive mobile layout.
- **3 test projects** — Unit tests (NumberFormatter, Business, GameEngine), integration tests (SQLite round-trip via InMemory), UI/ViewModel tests. All using xUnit + Shouldly + NSubstitute.

**Game features in v1**: 6 businesses (Lemonade Stand → Shrimp Boat), click-to-earn, managers for automation, offline earnings, prestige/angel investors (2% bonus each), auto-save every 5 seconds.

**CI/CD**: Single GitHub Actions workflow — build+test on every push, then desktop binaries (6 platforms) + signed Android APK → GitHub Release.

**Run it:**
```bash
cd ~/src/dotnet/MyAdventure
bash setup.sh
dotnet restore
dotnet build
dotnet test
dotnet run --project src/MyAdventure.Desktop
```

The `docs/KEYSTORE.md` file has the keystore generation and GitHub Secrets setup instructions. Everything is MIT licensed, all NuGet packages are MIT/Apache/BSD — free forever.






This is good progress. I have added the keystore and Github secrets. 
kushal@fedora:~/src/dotnet/MyAdventure$ git remote show origin 
* remote origin
  Fetch URL: git@github.com:kusl/MyAdventure.git
  Push  URL: git@github.com:kusl/MyAdventure.git
  HEAD branch: main
  Remote branches:
    dependabot/github_actions/actions/checkout-6          new (next fetch will store in remotes/origin)
    dependabot/github_actions/actions/download-artifact-7 new (next fetch will store in remotes/origin)
    dependabot/github_actions/actions/setup-dotnet-5      new (next fetch will store in remotes/origin)
    dependabot/github_actions/actions/setup-java-5        new (next fetch will store in remotes/origin)
    dependabot/github_actions/actions/upload-artifact-6   new (next fetch will store in remotes/origin)
    dependabot/nuget/microsoft-f078703165                 new (next fetch will store in remotes/origin)
    dependabot/nuget/opentelemetry-66d705222e             new (next fetch will store in remotes/origin)
    dependabot/nuget/testing-4256675e77                   new (next fetch will store in remotes/origin)
    main                                                  tracked
  Local branch configured for 'git pull':
    main merges with remote main
  Local ref configured for 'git push':
    main pushes to main (fast-forwardable)
kushal@fedora:~/src/dotnet/MyAdventure$ 
Lets address all our issues 
1. Please update our nuget dependencies. Give me the full props file and I will paste it
The given project `MyAdventure.Android` has no updates given the current sources.
Project `MyAdventure.Core` has the following updates to its packages
   [net10.0]: 
   Top-level Package        Requested   Resolved   Latest
   > OpenTelemetry.Api      1.11.2      1.11.2     1.15.0

The given project `MyAdventure.Desktop` has no updates given the current sources.
Project `MyAdventure.Infrastructure` has the following updates to its packages
   [net10.0]: 
   Top-level Package                            Requested   Resolved   Latest
   > OpenTelemetry                              1.11.2      1.11.2     1.15.0
   > OpenTelemetry.Exporter.Console             1.11.2      1.11.2     1.15.0
   > OpenTelemetry.Extensions.Hosting           1.11.2      1.11.2     1.15.0
   > OpenTelemetry.Instrumentation.Runtime      1.11.0      1.11.0     1.15.0

The given project `MyAdventure.Shared` has no updates given the current sources.
Project `MyAdventure.Core.Tests` has the following updates to its packages
   [net10.0]: 
   Top-level Package                Requested   Resolved   Latest
   > Bogus                          35.6.1      35.6.1     35.6.5
   > Microsoft.NET.Test.Sdk         17.13.0     17.13.0    18.0.1
   > xunit.runner.visualstudio      3.0.2       3.0.2      3.1.5 

Project `MyAdventure.Integration.Tests` has the following updates to its packages
   [net10.0]: 
   Top-level Package                Requested   Resolved   Latest
   > Microsoft.NET.Test.Sdk         17.13.0     17.13.0    18.0.1
   > xunit.runner.visualstudio      3.0.2       3.0.2      3.1.5 

Project `MyAdventure.UI.Tests` has the following updates to its packages
   [net10.0]: 
   Top-level Package                Requested   Resolved   Latest
   > Microsoft.NET.Test.Sdk         17.13.0     17.13.0    18.0.1
   > xunit.runner.visualstudio      3.0.2       3.0.2      3.1.5 

2. Fix the build 
  MyAdventure.Core net10.0 failed with 1 error(s) (0.1s)
    /home/kushal/src/dotnet/MyAdventure/src/MyAdventure.Core/Entities/GameState.cs(4,26): error CS8865: Only records may inherit from records.

Build failed with 1 error(s) in 1.0s

real	0m1.142s
user	0m1.350s
sys	0m0.261s
Restore complete (0.5s)
  MyAdventure.Core net10.0 failed with 1 error(s) (0.1s)
    /home/kushal/src/dotnet/MyAdventure/src/MyAdventure.Core/Entities/GameState.cs(4,26): error CS8865: Only records may inherit from records.

Build failed with 1 error(s) in 0.8s

3. Make any other changes that you see necessary to avoid another round trip. 
The latest code is in dump.txt now 
