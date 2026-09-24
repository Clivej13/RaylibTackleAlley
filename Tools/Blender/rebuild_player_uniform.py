"""Apply uniform to saved approved sources, export and validate via Blender MCP.

Run this after any upstream body/animation regeneration. Does not create actions.
"""
from pathlib import Path
import runpy, sys
sys.dont_write_bytecode=True
TOOLS=Path(__file__).resolve().parent
sys.path.insert(0,str(TOOLS))
for script in ('upgrade_player_uniform.py','upgrade_player_jersey.py',
               'validate_player_uniform.py','validate_player_jersey.py',
               'refine_player_silhouette.py','validate_player_silhouette.py'):
    runpy.run_path(str(TOOLS/script),run_name='__main__')
