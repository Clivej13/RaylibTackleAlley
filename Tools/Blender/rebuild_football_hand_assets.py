"""Rebuild this asset pass from the saved approved player clips through Blender MCP."""
from pathlib import Path
import runpy
import sys

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))

for script in ('create_football.py', 'upgrade_player_hands.py',
               'export_football_player.py', 'create_player_carry_animations.py',
               'validate_player_hands.py'):
    print('REBUILD_STAGE', script, flush=True)
    runpy.run_path(str(TOOLS / script), run_name='__main__')
print('FOOTBALL_HAND_ASSETS_COMPLETE')
