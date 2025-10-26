using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum TerrainType:byte
{
    Land,
    Ocean,
    Mountain,
    River
}

public enum BuildStatus:byte
{
    Buildable,
    Occupied,
    Restricted
}
