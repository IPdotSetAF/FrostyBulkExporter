using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace BulkExporterPlugin.Enums
{
    public enum MeshFormat
    {
        [Description(".FBX")]
        FBX,
        [Description(".OBJ")]
        OBJ
    }

    public enum MeshVersion
    {
        FBX_2012,
        FBX_2013,
        FBX_2014,
        FBX_2015,
        FBX_2016,
        FBX_2017
    }

    public enum MeshScale
    {
        Millimeters,
        Centimeters,
        Meters,
        Kilometers
    }

    public enum TextureFormat
    {
        [Description(".PNG")]
        PNG,
        [Description(".HDR")]
        HDR,
        [Description(".TGA")]
        TGA,
        [Description(".DDS")]
        DDS
    }
}
