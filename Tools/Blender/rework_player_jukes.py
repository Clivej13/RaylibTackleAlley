"""Build, validate and preview only the two braking/acceleration jukes via MCP."""
from pathlib import Path
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import create_player_evades as evades

evades.CONFIG = {name: cfg for name, cfg in evades.CONFIG.items() if cfg['kind'] == 'Juke'}
evades.PRE = evades.OUT / 'JukePreviews'

if __name__ == '__main__':
    if '--preview' in sys.argv:
        import preview_player_evades
        preview_player_evades.main()
    else:
        evades.main()
        import validate_player_evades
        validate_player_evades.main()
