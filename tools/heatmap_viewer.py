#!/usr/bin/env python3
"""
Read and visualize WatchtowerNetwork town distance heatmap binaries.

Binary format (little-endian):
- int32 stringLen + utf8 string: magic ("WNHM")
- uint16: formatVersion (v1, v2, ...)
- metadata payload (version dependent):
  - newer: string mapModuleId + string gameVersion
  - legacy: uint32 sceneXmlCrc + string gameVersion
- int32: gridWidth
- int32: gridHeight
- float32: gridStep
- float32: minX
- float32: minY
- float32: maxX
- float32: maxY
- int32: cellCount
- repeated cellCount:
  - uint16: posX
  - uint16: posY
  - bool (uint8): isLand
  - uint8: distance
"""

from __future__ import annotations

import argparse
import re
import struct
from pathlib import Path
from typing import BinaryIO, Dict, List, Tuple
import xml.etree.ElementTree as ET

import matplotlib.pyplot as plt
import numpy as np


def read_int32(stream: BinaryIO) -> int:
    data = stream.read(4)
    if len(data) != 4:
        raise EOFError("Unexpected end of file while reading int32.")
    return struct.unpack("<i", data)[0]


def read_uint16(stream: BinaryIO) -> int:
    data = stream.read(2)
    if len(data) != 2:
        raise EOFError("Unexpected end of file while reading uint16.")
    return struct.unpack("<H", data)[0]


def read_uint32(stream: BinaryIO) -> int:
    data = stream.read(4)
    if len(data) != 4:
        raise EOFError("Unexpected end of file while reading uint32.")
    return struct.unpack("<I", data)[0]


def read_float32(stream: BinaryIO) -> float:
    data = stream.read(4)
    if len(data) != 4:
        raise EOFError("Unexpected end of file while reading float32.")
    return struct.unpack("<f", data)[0]


def read_string(stream: BinaryIO) -> str:
    length = read_int32(stream)
    if length < 0 or length > 1024 * 1024:
        raise ValueError(f"Invalid string length: {length}")
    data = stream.read(length)
    if len(data) != length:
        raise EOFError("Unexpected end of file while reading string.")
    return data.decode("utf-8")


def looks_like_game_version(value: str) -> bool:
    # Accept both game-style and module-style version tags:
    #  - 1.3.15
    #  - v1.1.3.110062
    if not value:
        return False
    text = value.strip()
    return re.match(r"^[A-Za-z]?\d+(\.\d+){1,4}$", text) is not None


def try_parse_new_metadata(stream: BinaryIO) -> Tuple[str, str]:
    map_module_id = read_string(stream)
    game_version = read_string(stream)
    if not map_module_id:
        raise ValueError("Empty map module id in metadata.")
    if not looks_like_game_version(game_version):
        raise ValueError(f"Unexpected game version string: {game_version}")
    return map_module_id, game_version


def try_parse_legacy_metadata(stream: BinaryIO) -> Tuple[int, str]:
    scene_xml_crc = read_uint32(stream)
    game_version = read_string(stream)
    if not looks_like_game_version(game_version):
        raise ValueError(f"Unexpected game version string: {game_version}")
    return scene_xml_crc, game_version


def parse_heatmap(path: Path) -> Tuple[Dict[str, object], np.ndarray, np.ndarray]:
    with path.open("rb") as f:
        magic = read_string(f)
        if magic != "WNHM":
            raise ValueError(f"Invalid magic value: {magic}")

        format_version = read_uint16(f)
        if format_version < 1:
            raise ValueError(f"Unsupported format version: {format_version}")

        metadata_offset = f.tell()
        map_module_id = None
        scene_xml_crc = None
        game_version = ""

        # Newer format: mapModuleId + gameVersion.
        # Fallback to legacy format: sceneXmlCrc + gameVersion.
        try:
            map_module_id, game_version = try_parse_new_metadata(f)
        except Exception:
            f.seek(metadata_offset)
            scene_xml_crc, game_version = try_parse_legacy_metadata(f)

        grid_width = read_int32(f)
        grid_height = read_int32(f)
        grid_step = read_float32(f)
        min_x = read_float32(f)
        min_y = read_float32(f)
        max_x = read_float32(f)
        max_y = read_float32(f)
        cell_count = read_int32(f)

        expected_count = grid_width * grid_height
        if cell_count != expected_count:
            raise ValueError(
                f"Cell count mismatch. Header says {cell_count}, expected {expected_count}."
            )

        is_land = np.zeros((grid_height, grid_width), dtype=bool)
        distance = np.zeros((grid_height, grid_width), dtype=np.uint8)

        for _ in range(cell_count):
            pos_x = read_uint16(f)
            pos_y = read_uint16(f)
            land_byte = f.read(1)
            dist_byte = f.read(1)
            if len(land_byte) != 1 or len(dist_byte) != 1:
                raise EOFError("Unexpected end of file while reading cell.")

            if pos_x >= grid_width or pos_y >= grid_height:
                continue

            is_land[pos_y, pos_x] = bool(land_byte[0])
            distance[pos_y, pos_x] = dist_byte[0]

    meta = {
        "format_version": format_version,
        "scene_xml_crc": scene_xml_crc,
        "map_module_id": map_module_id,
        "game_version": game_version,
        "grid_width": grid_width,
        "grid_height": grid_height,
        "grid_step": grid_step,
        "min_x": min_x,
        "min_y": min_y,
        "max_x": max_x,
        "max_y": max_y,
        "cell_count": cell_count,
    }
    return meta, is_land, distance


def build_rgb_image(is_land: np.ndarray, distance: np.ndarray) -> np.ndarray:
    height, width = distance.shape
    rgb = np.zeros((height, width, 3), dtype=np.uint8)

    # Distinct sea color.
    rgb[~is_land] = np.array([20, 55, 130], dtype=np.uint8)

    # Land: colorize by normalized distance (0..255).
    land_mask = is_land
    land_values = distance.astype(np.float32) / 255.0
    land_values = np.clip(land_values, 0.0, 1.0)

    red = (255.0 * land_values).astype(np.uint8)
    green = (255.0 * (1.0 - np.abs(land_values - 0.5) * 2.0)).astype(np.uint8)
    blue = (255.0 * (1.0 - land_values)).astype(np.uint8)

    rgb[..., 0][land_mask] = red[land_mask]
    rgb[..., 1][land_mask] = green[land_mask]
    rgb[..., 2][land_mask] = blue[land_mask]
    return rgb


def parse_towns_from_xml(xml_path: Path) -> List[Tuple[str, float, float]]:
    tree = ET.parse(xml_path)
    root = tree.getroot()
    towns: List[Tuple[str, float, float]] = []

    for settlement in root.findall(".//Settlement"):
        settlement_id = settlement.attrib.get("id", "")
        # Primary filter: vanilla town ids.
        if settlement_id.startswith("town_"):
            x_raw = settlement.attrib.get("posX")
            y_raw = settlement.attrib.get("posY")
            if x_raw is None or y_raw is None:
                continue
            towns.append((settlement_id, float(x_raw), float(y_raw)))

    # Fallback for custom XMLs that may not use town_* ids.
    if towns:
        return towns

    for settlement in root.findall(".//Settlement"):
        town_node = settlement.find("./Components/Town")
        if town_node is None:
            continue

        is_castle = town_node.attrib.get("is_castle", "false").lower() == "true"
        if is_castle:
            continue

        x_raw = settlement.attrib.get("posX")
        y_raw = settlement.attrib.get("posY")
        if x_raw is None or y_raw is None:
            continue

        settlement_id = settlement.attrib.get("id", "unknown_town")
        towns.append((settlement_id, float(x_raw), float(y_raw)))

    return towns


def parse_watchtowers_from_xml(xml_path: Path) -> List[Tuple[str, float, float]]:
    tree = ET.parse(xml_path)
    root = tree.getroot()
    watchtowers: List[Tuple[str, float, float]] = []

    for settlement in root.findall(".//Settlement"):
        settlement_id = settlement.attrib.get("id", "")
        if "watchtower" not in settlement_id:
            continue

        x_raw = settlement.attrib.get("posX")
        y_raw = settlement.attrib.get("posY")
        if x_raw is None or y_raw is None:
            continue

        watchtowers.append((settlement_id, float(x_raw), float(y_raw)))

    return watchtowers


def world_to_grid(meta: Dict[str, object], world_x: float, world_y: float) -> Tuple[float, float]:
    min_x = float(meta["min_x"])
    min_y = float(meta["min_y"])
    step = float(meta["grid_step"])
    grid_x = (world_x - min_x) / step
    grid_y = (world_y - min_y) / step
    return grid_x, grid_y


def main() -> None:
    parser = argparse.ArgumentParser(description="Watchtower heatmap binary viewer")
    parser.add_argument("input", type=Path, help="Path to heatmap .bin file")
    parser.add_argument("--output", type=Path, default=None, help="Optional output PNG path")
    parser.add_argument("--no-show", action="store_true", help="Do not open a preview window")
    parser.add_argument(
        "--towns-xml",
        type=Path,
        default=None,
        help="Optional settlements XML file; towns will be plotted on top of the heatmap",
    )
    parser.add_argument(
        "--town-labels",
        action="store_true",
        help="Show settlement IDs next to plotted towns (requires --towns-xml)",
    )
    parser.add_argument(
        "--watchtowers-xml",
        type=Path,
        default=None,
        help="Optional watchtower settlements XML file to overlay generated watchtower positions",
    )
    parser.add_argument(
        "--watchtower-labels",
        action="store_true",
        help="Show watchtower settlement IDs next to watchtower markers (requires --watchtowers-xml)",
    )
    args = parser.parse_args()

    meta, is_land, distance = parse_heatmap(args.input)
    image = build_rgb_image(is_land, distance)

    print("Heatmap metadata:")
    print(f"  format_version: {meta['format_version']}")
    if meta["map_module_id"] is not None:
        print(f"  map_module_id: {meta['map_module_id']}")
    if meta["scene_xml_crc"] is not None:
        print(f"  scene_xml_crc: 0x{meta['scene_xml_crc']:08X}")
    print(f"  game_version:  {meta['game_version']}")
    print(f"  grid:          {meta['grid_width']} x {meta['grid_height']}")
    print(f"  step:          {meta['grid_step']}")
    print(f"  bounds:        ({meta['min_x']:.2f}, {meta['min_y']:.2f}) -> ({meta['max_x']:.2f}, {meta['max_y']:.2f})")
    towns: List[Tuple[str, float, float]] = []
    if args.towns_xml is not None:
        towns = parse_towns_from_xml(args.towns_xml)
        print(f"  towns plotted: {len(towns)} (from {args.towns_xml})")
    watchtowers: List[Tuple[str, float, float]] = []
    if args.watchtowers_xml is not None:
        watchtowers = parse_watchtowers_from_xml(args.watchtowers_xml)
        print(f"  watchtowers plotted: {len(watchtowers)} (from {args.watchtowers_xml})")

    fig, ax = plt.subplots(figsize=(12, 8))
    ax.set_title("Town Distance Heatmap (land distance, sea masked)")
    ax.imshow(image, origin="lower")
    ax.set_xlabel("pos_x")
    ax.set_ylabel("pos_y")

    if towns:
        xs: List[float] = []
        ys: List[float] = []
        labels: List[str] = []
        for town_id, world_x, world_y in towns:
            grid_x, grid_y = world_to_grid(meta, world_x, world_y)
            xs.append(grid_x)
            ys.append(grid_y)
            labels.append(town_id)

        ax.scatter(xs, ys, s=30, c="white", marker="o", edgecolors="black", linewidths=0.5, zorder=4)
        if args.town_labels:
            for i, label in enumerate(labels):
                ax.text(xs[i] + 2, ys[i] + 2, label, color="white", fontsize=7, zorder=5)

    if watchtowers:
        w_xs: List[float] = []
        w_ys: List[float] = []
        w_labels: List[str] = []
        for wt_id, world_x, world_y in watchtowers:
            grid_x, grid_y = world_to_grid(meta, world_x, world_y)
            w_xs.append(grid_x)
            w_ys.append(grid_y)
            w_labels.append(wt_id)

        ax.scatter(w_xs, w_ys, s=55, c="magenta", marker="^", edgecolors="black", linewidths=0.7, zorder=6)
        if args.watchtower_labels:
            for i, label in enumerate(w_labels):
                ax.text(w_xs[i] + 2, w_ys[i] + 2, label, color="magenta", fontsize=7, zorder=7)

    fig.tight_layout()

    if args.output is not None:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        fig.savefig(args.output, dpi=150)
        print(f"Saved PNG to: {args.output}")

    if not args.no_show:
        plt.show()
    else:
        plt.close(fig)


if __name__ == "__main__":
    main()
