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

