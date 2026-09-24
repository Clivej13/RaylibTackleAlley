"""Compatibility entry point for the current stadium access refactor."""
from pathlib import Path
import runpy
if __name__ == "__main__":
    runpy.run_path(str(Path(__file__).with_name("refactor_stadium_access.py")), run_name="__main__")
