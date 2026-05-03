# Setup askpass:

## 1. Install askpass

```
sudo apt install ssh-askpass
```

## 2. Check path

```
command -v ssh-askpass
```

should return

```
/usr/bin/ssh-askpass
```

## 3. Create wrapper script for VSCode to use askpass

```
mkdir -p ~/.local/bin
nano ~/.local/bin/vscode-sudo-askpass
```

Content of vscode-sudo-askpass:

```
#!/usr/bin/env bash
export SUDO_ASKPASS=/usr/bin/ssh-askpass
exec sudo -A -E "$@"
```

## 4. Make it executable

```
chmod +x ~/.local/bin/vscode-sudo-askpass
```

## 5. Adjust launch.json
Element "pipeTransport" -> "debuggerPath" must be adjusted for your system.
