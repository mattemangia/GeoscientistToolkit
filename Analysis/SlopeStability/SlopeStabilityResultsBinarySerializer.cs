using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    public static class SlopeStabilityResultsBinarySerializer
    {
        private const string MagicHeader = "GTSR";
        private const int CurrentVersion = 1;

        public static void Write(string path, SlopeStabilityResults results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);

            writer.Write(MagicHeader);
            writer.Write(CurrentVersion);

            WriteResults(writer, results);
        }

        public static SlopeStabilityResults Read(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var magic = new string(reader.ReadChars(4));
            if (!string.Equals(magic, MagicHeader, StringComparison.Ordinal))
                throw new InvalidDataException("Unrecognized slope stability results file.");

            var version = reader.ReadInt32();
            if (version != CurrentVersion)
                throw new InvalidDataException($"Unsupported slope stability results version: {version}.");

            return ReadResults(reader);
        }

        private static void WriteResults(BinaryWriter writer, SlopeStabilityResults results)
        {
            writer.Write(results.SimulationDate.ToBinary());
            writer.Write(results.TotalSimulationTime);
            writer.Write(results.TotalSteps);
            writer.Write(results.Converged);
            writer.Write(results.StatusMessage ?? string.Empty);

            writer.Write(results.MaxDisplacement);
            writer.Write(results.MeanDisplacement);
            writer.Write(results.NumFailedBlocks);
            writer.Write(results.NumSlidingContacts);
            writer.Write(results.NumOpenedJoints);
            writer.Write(results.SafetyFactor);
            writer.Write(results.SafetyFactorComputed);

            writer.Write(results.KineticEnergy);
            writer.Write(results.PotentialEnergy);
            writer.Write(results.DissipatedEnergy);

            writer.Write(results.ComputationTimeSeconds);
            writer.Write(results.AverageTimePerStep);

            WriteBlockResults(writer, results.BlockResults);
            WriteContactResults(writer, results.ContactResults);

            writer.Write(results.HasTimeHistory);
            if (results.HasTimeHistory)
                WriteTimeHistory(writer, results.TimeHistory);
        }

        private static SlopeStabilityResults ReadResults(BinaryReader reader)
        {
            var results = new SlopeStabilityResults
            {
                SimulationDate = DateTime.FromBinary(reader.ReadInt64()),
                TotalSimulationTime = reader.ReadSingle(),
                TotalSteps = reader.ReadInt32(),
                Converged = reader.ReadBoolean(),
                StatusMessage = reader.ReadString(),
                MaxDisplacement = reader.ReadSingle(),
                MeanDisplacement = reader.ReadSingle(),
                NumFailedBlocks = reader.ReadInt32(),
                NumSlidingContacts = reader.ReadInt32(),
                NumOpenedJoints = reader.ReadInt32(),
                SafetyFactor = reader.ReadSingle(),
                SafetyFactorComputed = reader.ReadBoolean(),
                KineticEnergy = reader.ReadSingle(),
                PotentialEnergy = reader.ReadSingle(),
                DissipatedEnergy = reader.ReadSingle(),
                ComputationTimeSeconds = reader.ReadSingle(),
                AverageTimePerStep = reader.ReadSingle()
            };

            results.BlockResults = ReadBlockResults(reader);
            results.ContactResults = ReadContactResults(reader);

            results.HasTimeHistory = reader.ReadBoolean();
            if (results.HasTimeHistory)
                results.TimeHistory = ReadTimeHistory(reader);
            else
                results.TimeHistory = new List<TimeSnapshot>();

            return results;
        }

        private static void WriteBlockResults(BinaryWriter writer, List<BlockResult> blockResults)
        {
            writer.Write(blockResults?.Count ?? 0);
            if (blockResults == null) return;

            foreach (var block in blockResults)
            {
                writer.Write(block.BlockId);
                WriteVector3(writer, block.InitialPosition);
                WriteVector3(writer, block.FinalPosition);
                WriteVector3(writer, block.Displacement);
                WriteVector3(writer, block.Velocity);
                WriteQuaternion(writer, block.FinalOrientation);
                WriteVector3(writer, block.AngularVelocity);
                writer.Write(block.Mass);
                writer.Write(block.HasFailed);
                writer.Write(block.IsFixed);
                writer.Write(block.NumContacts);
            }
        }

        private static List<BlockResult> ReadBlockResults(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var results = new List<BlockResult>(count);

            for (int i = 0; i < count; i++)
            {
                var block = new BlockResult
                {
                    BlockId = reader.ReadInt32(),
                    InitialPosition = ReadVector3(reader),
                    FinalPosition = ReadVector3(reader),
                    Displacement = ReadVector3(reader),
                    Velocity = ReadVector3(reader),
                    FinalOrientation = ReadQuaternion(reader),
                    AngularVelocity = ReadVector3(reader),
                    Mass = reader.ReadSingle(),
                    HasFailed = reader.ReadBoolean(),
                    IsFixed = reader.ReadBoolean(),
                    NumContacts = reader.ReadInt32()
                };
                results.Add(block);
            }

            return results;
        }

        private static void WriteContactResults(BinaryWriter writer, List<ContactResult> contactResults)
        {
            writer.Write(contactResults?.Count ?? 0);
            if (contactResults == null) return;

            foreach (var contact in contactResults)
            {
                writer.Write(contact.BlockAId);
                writer.Write(contact.BlockBId);
                WriteVector3(writer, contact.ContactPoint);
                WriteVector3(writer, contact.ContactNormal);
                writer.Write(contact.MaxNormalForce);
                writer.Write(contact.MaxShearForce);
                writer.Write(contact.HasSlipped);
                writer.Write(contact.HasOpened);
                writer.Write(contact.TotalSlipDisplacement);
                writer.Write(contact.IsJointContact);
                writer.Write(contact.JointSetId);
            }
        }

        private static List<ContactResult> ReadContactResults(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var results = new List<ContactResult>(count);

            for (int i = 0; i < count; i++)
            {
                var contact = new ContactResult
                {
                    BlockAId = reader.ReadInt32(),
                    BlockBId = reader.ReadInt32(),
                    ContactPoint = ReadVector3(reader),
                    ContactNormal = ReadVector3(reader),
                    MaxNormalForce = reader.ReadSingle(),
                    MaxShearForce = reader.ReadSingle(),
                    HasSlipped = reader.ReadBoolean(),
                    HasOpened = reader.ReadBoolean(),
                    TotalSlipDisplacement = reader.ReadSingle(),
                    IsJointContact = reader.ReadBoolean(),
                    JointSetId = reader.ReadInt32()
                };
                results.Add(contact);
            }

            return results;
        }

        private static void WriteTimeHistory(BinaryWriter writer, List<TimeSnapshot> snapshots)
        {
            writer.Write(snapshots?.Count ?? 0);
            if (snapshots == null) return;

            foreach (var snapshot in snapshots)
            {
                writer.Write(snapshot.Time);
                writer.Write(snapshot.KineticEnergy);
                writer.Write(snapshot.PotentialEnergy);

                WriteVector3List(writer, snapshot.BlockPositions);
                WriteQuaternionList(writer, snapshot.BlockOrientations);
                WriteVector3List(writer, snapshot.BlockVelocities);
            }
        }

        private static List<TimeSnapshot> ReadTimeHistory(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var snapshots = new List<TimeSnapshot>(count);

            for (int i = 0; i < count; i++)
            {
                var snapshot = new TimeSnapshot
                {
                    Time = reader.ReadSingle(),
                    KineticEnergy = reader.ReadSingle(),
                    PotentialEnergy = reader.ReadSingle(),
                    BlockPositions = ReadVector3List(reader),
                    BlockOrientations = ReadQuaternionList(reader),
                    BlockVelocities = ReadVector3List(reader)
                };
                snapshots.Add(snapshot);
            }

            return snapshots;
        }

        private static void WriteVector3(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
            writer.Write(value.W);
        }

        private static Quaternion ReadQuaternion(BinaryReader reader)
        {
            return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static void WriteVector3List(BinaryWriter writer, List<Vector3> values)
        {
            writer.Write(values?.Count ?? 0);
            if (values == null) return;

            foreach (var value in values)
                WriteVector3(writer, value);
        }

        private static List<Vector3> ReadVector3List(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var values = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
                values.Add(ReadVector3(reader));
            return values;
        }

        private static void WriteQuaternionList(BinaryWriter writer, List<Quaternion> values)
        {
            writer.Write(values?.Count ?? 0);
            if (values == null) return;

            foreach (var value in values)
                WriteQuaternion(writer, value);
        }

        private static List<Quaternion> ReadQuaternionList(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var values = new List<Quaternion>(count);
            for (int i = 0; i < count; i++)
                values.Add(ReadQuaternion(reader));
            return values;
        }
    }
}
