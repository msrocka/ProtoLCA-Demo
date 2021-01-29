﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Grpc.Core;
using ProtoLCA;
using ProtoLCA.Services;

using static DemoApp.Util;

namespace DemoApp
{
    class Program
    {
        static void Main()
        {
            var chan = new Channel(
                "localhost:8080",
                ChannelCredentials.Insecure);
            // Task.Run(() => UnitMappingExample(chan)).Wait();
            Task.Run(() => FlowMappingExample(chan)).Wait();
            // Task.Run(() => CreateExampleProcess(chan)).Wait();
            // Task.Run(() => PrintAllMappingFiles(chan)).Wait();
            Console.ReadKey();
        }

        /// <summary>
        /// Shows the mapping of flows. It first checks the mapping file
        /// in openLCA (which is created if it does not exist) for a
        /// matching entry. If this does not exist, it searches for a
        /// matching existing flow and creates it if no matching flow
        /// can be found. Finally, it updates the mapping and returns
        /// the corresponding flow mapping. Note that in a real
        /// application you probably want to separate these steps. See
        /// the `FlowFetch` for implementation details.
        /// </summary>
        private static async void FlowMappingExample(Channel chan)
        {
            var flows = await FlowFetch.Create(
                chan, mapping: "ProtoLCA-Demo.csv");

            // this should find a matching flow in the reference data
            var entry = await flows.Get(
                FlowQuery.ForElementary("Carbon dioxide")
                .WithUnit("g")
                .WithCategory("air/unspecified"));

            Log("Carbon dioxide | g | air/unspecified is mapped to:");
            Log($"  flow: {entry.To.Flow.Name}");
            Log($"  category: {entry.To.Flow.CategoryPath}");
            Log($"  unit: {entry.To.Flow.RefUnit}");
            Log($"  conversion factor: {entry.ConversionFactor}\n");

            // this should create a new flow
            entry = await flows.Get(
                FlowQuery.ForElementary("SARS-CoV-2 viruses")
                .WithUnit("Item(s)")
                .WithCategory("air/urban"));

            Log("SARS-CoV-2 viruses | Item(s) | air/unspecified is mapped to:");
            Log($"  flow: {entry.To.Flow.Name}");
            Log($"  category: {entry.To.Flow.CategoryPath}");
            Log($"  unit: {entry.To.Flow.RefUnit}");
            Log($"  conversion factor: {entry.ConversionFactor}\n");
        }

        /// <summary>
        /// Shows how to map a unit name to the corresponding unit,
        /// unit group, and flow property objects in openLCA.
        /// </summary>
        private static async void UnitMappingExample(Channel chan)
        {
            var index = await UnitIndex.Build(chan);
            var tons = index.EntryOf("t");
            Log($"unit: {tons.Unit.Name}");
            Log($"unit group: {tons.UnitGroup.Name}");
            Log($"flow property (quantity): {tons.FlowProperty.Name}");
            Log($"conversion factor: {tons.Factor}");
        }

        private static async void PrintAllMappingFiles(Channel chan)
        {
            var service = new FlowMapService.FlowMapServiceClient(chan);
            var mappings = service.GetAll(new Empty()).ResponseStream;
            while (await mappings.MoveNext())
            {
                var name = mappings.Current.Name;
                Log($"Found mapping: {name}");
            }
        }

        private static async void CreateExampleProcess(Channel chan)
        {
            var flows = await FlowFetch.Create(chan, "ProtoLCA-Demo.csv");
            var data = new DataService.DataServiceClient(chan);

            // set the location
            var process = Build.ProcessOf("Iron Process - Gas cleaning");
            var location = GetLocation(data, "Global");
            process.Location = new Ref
            {
                Id = location.Id,
                Name = location.Name,
            };

            // add the exchanges
            Exchange qRef = null;
            foreach (var e in GetExampleExchanges())
            {
                var (isInput, type, name, amount, unit) = e;

                // find and map a flow
                var flowQuery = FlowQuery.For(type, name).WithUnit(unit);
                if (type == FlowType.ElementaryFlow)
                {
                    flowQuery.WithCategory("air/unspecified");
                }
                else
                {
                    flowQuery.WithLocation("FI");
                }
                var mapping = await flows.Get(flowQuery);
                if (mapping == null)
                    continue;

                process.LastInternalId += 1;
                var target = mapping.To;
                var exchange = new Exchange
                {
                    InternalId = process.LastInternalId,
                    Amount = amount * mapping.ConversionFactor,
                    Input = isInput,
                    Flow = target.Flow,
                    FlowProperty = target.FlowProperty,
                    Unit = target.Unit,
                };

                // set the provider for product inputs or waste outputs
                if (((isInput && type == FlowType.ProductFlow)
                    || (!isInput && type == FlowType.WasteFlow))
                    && target.Provider != null)
                {
                    exchange.DefaultProvider = target.Provider;
                }

                // set the quantitative reference
                if (!isInput && type == FlowType.ProductFlow)
                {
                    exchange.QuantitativeReference = true;
                    qRef = exchange;
                }

                process.Exchanges.Add(exchange);
            }

            // insert the process
            var insertStatus = data.PutProcess(process);
            if (!insertStatus.Ok)
                throw new Exception(insertStatus.Error);

            // calculation

            // select the first best LCIA method if it exsists
            Ref method = null;
            var methods = data.GetDescriptors(
                new DescriptorRequest { Type = ModelType.ImpactMethod })
                .ResponseStream;
            if (await methods.MoveNext())
            {
                method = methods.Current;
                Log($"Selected LCIA method: {method.Name}");
            }

            var setup = new CalculationSetup
            {
                Amount = qRef.Amount,
                ProductSystem = new Ref { Id = process.Id },
                ImpactMethod = method,
                FlowProperty = qRef.FlowProperty,
                Unit = qRef.Unit,
            };

            Log("Calculate results ...");
            var results = new ResultService.ResultServiceClient(chan);
            var resultStatus = results.Calculate(setup);
            if (!resultStatus.Ok)
            {
                Log($"ERROR: Failed to calculate " +
                    $"result: {resultStatus.Error}");
                return;
            }

            var impacts = results.GetImpacts(resultStatus.Result)
                .ResponseStream;
            var hasImpacts = false;
            while (await impacts.MoveNext())
            {
                var impact = impacts.Current;
                Log($"{impact.ImpactCategory.Name}: {impact.Value}" +
                    $" {impact.ImpactCategory.RefUnit}");
                hasImpacts = true;
            }

            if (!hasImpacts)
            {
                // TODO: show inventory results

            }

            results.Dispose(resultStatus.Result);
        }

        private static List<(bool, FlowType, string, double, string)>
            GetExampleExchanges()
        {
            var p = FlowType.ProductFlow;
            var w = FlowType.WasteFlow;
            var e = FlowType.ElementaryFlow;
            var i = true; // is input
            var o = false; // is output

            return new List<(bool, FlowType, string, double, string)> {
                (i, p, "Air Blast", 245.8751543969349, "t"),
                (i, w, "Combustion Air", 59.764430236449158, "t"),
                (i, p, "Hematite Pellets", 200, "t"),
                (i, p, "Coke", 50, "t"),
                (i, p, "Limestone", 30.422441963816247, "t"),
                (i, w, "Steel Scrap", 1.8853256607049331, "t"),
                (i, p, "Reductant", 16, "t"),
                (i, p, "Washing Solution", 75, "t"),

                (o, w, "Slag", 33.573534216580185, "t"),
                (o, e, "Carbon dioxide", 140.44236409682583, "t"),
                (o, e, "Water vapour", 30.591043638569072, "t"),
                (o, e, "Sulfur dioxide", 0.01134867565288134, "t"),
                (o, e, "Air", 158.58576460676247, "t"),
                (o, p, "Pig Iron", 138.2370620852756, "t"),
                (o, w, "Heat Loss", 32727.272727272728, "kWh"),
                (o, e, "Coarse Dust", 1.4340290871696806, "kg"),
                (o, w, "Scrubber Sludge", 56.261517810249792, "kg"),
                (o, e, "Fine Dust", 0.18398927491951844, "kg"),
            };
        }

        private static Location GetLocation(
            DataService.DataServiceClient client,
            string name)
        {
            var status = client.GetLocation(Build.RefOf(name: name));
            if (status.Ok)
                return status.Location;
            var location = Build.LocationOf(name);
            var insertStatus = client.PutLocation(location);
            if (!insertStatus.Ok)
                throw new Exception(insertStatus.Error);
            return location;
        }
    }
}
