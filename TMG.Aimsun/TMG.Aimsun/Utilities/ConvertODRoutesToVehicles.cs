/*
    Copyright 2017 Travel Modelling Group, Department of Civil Engineering, University of Toronto

    This file is part of XTMF.

    XTMF is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Datastructure;
using XTMF;
using TMG.Input;

namespace TMG.Aimsun.Utilities
{
    public class ConvertODRoutesToVehicles : ISelfContainedModule
    {
        public bool RuntimeValidation(ref string error)
        {
            return true;
        }

        [SubModelInformation(Required = true, Description = "The trajectory file (*.trj)")]
        public FileLocation TrajectoryOutput;

        [SubModelInformation(Required = true, Description = "The location for the resulting file.")]
        public FileLocation OutputFile;

        [RunParameter("Binary Format", false, "Is the input trajectory file a binary output?")]
        public bool Binary;

        public string Name { get; set; }
        public float Progress => (float)(Position / (double)TotalSize);


        public Tuple<byte, byte, byte> ProgressColour { get; } = new Tuple<byte, byte, byte>(50, 150, 50);

        private long TotalSize;

        private long Position;

        private Dictionary<int, List<VehicleStatus>> StatusByVehicle;

        public void Start()
        {
            if (Binary)
            {
                ProcessBinary();
            }
            else
            {
                ProcessText();
            }
        }

        private void ProcessBinary()
        {
            throw new XTMFRuntimeException("Not Implemented!");
        }

        private void ProcessText()
        {
            StatusByVehicle = new Dictionary<int, List<VehicleStatus>>();
            const int expectedcolumns = 1;
            Status = "Loading Data";
            using (var reader = new CsvReader(TrajectoryOutput, true))
            {
                //FORMAT:
                //
                var baseStream = reader.BaseStream;
                int columns;
                BoundingBox boundingBox;
                TotalSize = baseStream.Length;
                Position = 0;
                LoadFormatForText(reader);
                LoadDimensionsForText(reader, out boundingBox);
                float currentTime = 0.0f;
                while (reader.LoadLine(out columns))
                {
                    if (columns >= expectedcolumns)
                    {
                        Position = baseStream.Position;
                        // determine if this is a vehicle record or a time step
                        int recordType;
                        reader.Get(out recordType, 0);
                        switch (recordType)
                        {
                            // Time Step
                            case 2:
                                {
                                    //update the time (this should get the third column due to the "***timestep"
                                    reader.LoadLine(out columns);
                                    reader.Get(out currentTime, 0);
                                }
                                break;
                            // Vehicle
                            case 3:
                                {
                                    int vehicleID, linkID;
                                    float speed, acceleration;
                                    reader.LoadLine(out columns);
                                    reader.Get(out vehicleID, 0);
                                    reader.LoadLine(out columns);
                                    reader.Get(out linkID, 0);
                                    reader.LoadLine(out columns);
                                    reader.LoadLine(out columns);
                                    reader.LoadLine(out columns);
                                    reader.LoadLine(out columns);
                                    reader.LoadLine(out columns);
                                    reader.LoadLine(out columns);
                                    reader.LoadLine(out columns);
                                    reader.LoadLine(out columns);
                                    reader.Get(out speed, 0);
                                    reader.LoadLine(out columns);
                                    reader.Get(out acceleration, 0);
                                    List<VehicleStatus> vehicle;
                                    var status = new VehicleStatus(linkID, speed, acceleration, currentTime);
                                    if (!StatusByVehicle.TryGetValue(vehicleID, out vehicle))
                                    {
                                        vehicle = new List<VehicleStatus>();
                                        StatusByVehicle.Add(vehicleID, vehicle);
                                    }
                                    vehicle.Add(status);
                                }
                                break;
                            default:
                                throw new XTMFRuntimeException($"In {Name} we found a invalid record type!");
                        }
                    }
                }
                // play nice with the Progress
                Position = TotalSize;
            }
            Status = "Processing Data";
            using (var writer = new StreamWriter(OutputFile))
            {
                writer.WriteLine("UID,VehicleID,LinkID,Timestep,Speed,Acceleration");
                long uid = 0;
                int lastVehicle = -1, lastLinkID = -1;
                foreach (var vehicle in StatusByVehicle.OrderBy(v => v.Key))
                {
                    bool incrementedUid = false;
                    if (vehicle.Key != lastVehicle)
                    {
                        lastVehicle = vehicle.Key;
                        uid++;
                        incrementedUid = true;
                    }
                    var vehicleStr = vehicle.Key.ToString();
                    foreach (var record in vehicle.Value)
                    {
                        if (record.LinkID != lastLinkID)
                        {
                            lastLinkID = record.LinkID;
                            if (!incrementedUid)
                            {
                                uid++;
                            }
                        }
                        writer.Write(uid);
                        writer.Write(',');
                        writer.Write(vehicleStr);
                        writer.Write(',');
                        writer.Write(record.LinkID);
                        writer.Write(',');
                        writer.Write(record.TimeStep);
                        writer.Write(',');
                        writer.Write(record.Speed);
                        writer.Write(',');
                        writer.WriteLine(record.Acceleration);
                        incrementedUid = false;
                    }
                }
            }
        }

        class VehicleStatus
        {
            internal readonly int LinkID;
            internal readonly float Speed, Acceleration;
            internal readonly float TimeStep;
            public VehicleStatus(int linkID, float speed, float acceleration, float timeStep)
            {
                LinkID = linkID;
                Speed = speed;
                Acceleration = acceleration;
                TimeStep = timeStep;
            }
        }

        private void LoadFormatForText(CsvReader reader)
        {
            //Header = 0
            //Format L or B (Endian) -- as a number for some reason
            //Version
            int columns;
            int recordType, endian;
            float version;
            reader.LoadLine(out columns);
            reader.Get(out recordType, 0);
            reader.LoadLine(out columns);
            reader.Get(out endian, 0);
            if (endian != 76)
            {
                throw new XTMFRuntimeException($"In {Name} the endian was not little endian when processing the trajectory file '{TrajectoryOutput}'!");
            }
            reader.LoadLine(out columns);
            reader.Get(out version, 0);
        }

        struct BoundingBox
        {
            internal readonly int MinX, MinY, MaxX, MaxY;
            public BoundingBox(int minX, int minY, int maxX, int maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }
        }

        private void LoadDimensionsForText(CsvReader reader, out BoundingBox bounds)
        {
            int columns;
            reader.LoadLine(out columns);
            reader.LoadLine(out columns);
            reader.LoadLine(out columns);
            // bounds
            int minX, minY, maxX, maxY;
            reader.LoadLine(out columns);
            reader.Get(out minX, 0);
            reader.LoadLine(out columns);
            reader.Get(out minY, 0);
            reader.LoadLine(out columns);
            reader.Get(out maxX, 0);
            reader.LoadLine(out columns);
            reader.Get(out maxY, 0);
            bounds = new BoundingBox(minX, minY, maxX, maxY);
        }

        private string Status;

        public override string ToString()
        {
            return Status ?? "Processing";
        }
    }
}
