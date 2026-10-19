"""Inspect the same validated records the KH3 AP client can consume."""

import argparse
import importlib.util
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bridge-dir", type=Path, required=True)
    parser.add_argument("--request-id", required=True)
    parser.add_argument("--song-id", type=int, required=True)
    parser.add_argument("--difficulty", type=int, choices=(0, 1, 2), default=0)
    parser.add_argument("--play-style", choices=("Normal", "OneButton", "Performer"), default="Normal")
    args = parser.parse_args()
    # Load the stdlib-only reader without importing every Archipelago world.
    path = Path(__file__).resolve().parent / "mom_results.py"
    spec = importlib.util.spec_from_file_location("mom_results", path)
    reader = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(reader)
    print(json.dumps(reader.read_song_results(
        args.bridge_dir, request_id=args.request_id,
        song_id=args.song_id, difficulty=args.difficulty,
        play_style=args.play_style,
    ), indent=2))


if __name__ == "__main__":
    main()
