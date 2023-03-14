// See https://aka.ms/new-console-template for more information

using Hl7.Fhir.Model;
using SixB.Hackathon;

Console.WriteLine("Hello, World!");

var service = new ObservationService();
await service.CreateObservation("123", "456", "789", "9234234599", 1.2m);
