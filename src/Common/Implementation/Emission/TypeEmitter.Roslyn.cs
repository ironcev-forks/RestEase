using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RestEase.Implementation.Analysis;
using RestEase.SourceGenerator.Implementation;
using static RestEase.SourceGenerator.Implementation.RoslynEmitUtils
    ;

namespace RestEase.Implementation.Emission
{
    internal class TypeEmitter
    {
        private readonly StringWriter stringWriter = new StringWriter();
        private readonly IndentedTextWriter writer;
        private readonly TypeModel typeModel;
        private readonly WellKnownSymbols wellKnownSymbols;
        private readonly int index;
        private readonly string namespaceName;
        private readonly string typeNamePrefix;
        private readonly string typeName;
        private readonly string qualifiedTypeName;
        private readonly string requesterFieldName;
        private readonly string? classHeadersFieldName;

        private readonly List<string> generatedFieldNames = new List<string>();

        public TypeEmitter(TypeModel typeModel, WellKnownSymbols wellKnownSymbols, int index)
        {
            this.typeModel = typeModel;
            this.wellKnownSymbols = wellKnownSymbols;
            this.index = index;
            this.writer = new IndentedTextWriter(this.stringWriter);

            string containingNamespace = this.typeModel.NamedTypeSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormats.Namespace);
            this.namespaceName = (string.IsNullOrEmpty(containingNamespace) ? "" : containingNamespace + ".") + "RestEaseGeneratedTypes";
            this.typeNamePrefix = "Implementation_" + this.index + "_";
            string constructorName = this.typeNamePrefix + this.typeModel.NamedTypeSymbol.ToDisplayString(SymbolDisplayFormats.ConstructorName);
            // They might have given the type a name like '@event', or it might have a type parameter like '@event'.
            // Therefore we need to escape the type parameters, but strip a leading @ from the class name
            this.typeName = this.typeNamePrefix +
                this.typeModel.NamedTypeSymbol.ToDisplayString(SymbolDisplayFormats.TypeNameWithConstraints).TrimStart('@');
            this.qualifiedTypeName = "global::" + this.namespaceName + "." + this.typeName;
            this.requesterFieldName = this.GenerateFieldName("requester");
            if (this.typeModel.HeaderAttributes.Count > 0)
            {
                this.classHeadersFieldName = this.GenerateFieldName("classHeaders");
            }

            this.AddClassDeclaration();
            this.AddInstanceCtor(constructorName);
            this.AddStaticCtor(constructorName);
        }

        private string GenerateFieldName(string baseName)
        {
            string? name = baseName;
            if (this.generatedFieldNames.Contains(name) || this.typeModel.NamedTypeSymbol.GetMembers().Any(x => x.Name == name))
            {
                int i = 1;
                do
                {
                    name = baseName + i;
                    i++;
                } while (this.generatedFieldNames.Contains(name) || this.typeModel.NamedTypeSymbol.GetMembers().Any(x => x.Name == name));
            }
            this.generatedFieldNames.Add(name);
            return name;
        }

        private void AddClassDeclaration()
        {
            string typeofInterfaceName = AddBareAngles(
                this.typeModel.NamedTypeSymbol,
                this.typeModel.NamedTypeSymbol.ToDisplayString(SymbolDisplayFormats.TypeofParameterNoTypeParameters));
            string interfaceName = this.typeModel.NamedTypeSymbol.ToDisplayString(SymbolDisplayFormats.ImplementedInterface);
            string typeofName = this.typeNamePrefix + AddBareAngles(
                this.typeModel.NamedTypeSymbol,
                this.typeModel.NamedTypeSymbol.ToDisplayString(SymbolDisplayFormats.TypofParameterNoTypeParametersNoQualificationNoEscape));

            // We don't want to get involved with NRTs. We'd need to know whether they were supported and switch our generation based on this, which
            // is too much hassle for something the user doesn't actually see (they see the nullability on the interface).
            this.writer.WriteLine("#nullable disable");
            this.writer.WriteLine("[assembly: global::RestEase.Implementation.RestEaseInterfaceImplementationAttribute(" +
                "typeof(" + typeofInterfaceName + "), typeof(global::" + this.namespaceName + "." + typeofName + "))]");

            this.writer.WriteLine("namespace " + this.namespaceName);
            this.writer.WriteLine("{");
            this.writer.Indent++;

            this.writer.WriteLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
            this.writer.WriteLine("[global::System.Runtime.CompilerServices.CompilerGenerated]");

            string interfaceNameWithConstraints = this.typeModel.NamedTypeSymbol.ToDisplayString(SymbolDisplayFormats.QualifiedTypeNameWithTypeConstraints);
            this.writer.WriteLine("internal class " + this.typeName + " : " + interfaceNameWithConstraints);

            this.writer.WriteLine("{");
            this.writer.Indent++;

            if (this.classHeadersFieldName != null)
            {
                this.writer.WriteLine("private static readonly global::System.Collections.Generic.KeyValuePair<string, string>[] " +
                    this.classHeadersFieldName + ";");
            }
            this.writer.WriteLine("private readonly global::RestEase.IRequester " + this.requesterFieldName + ";");
        }

        private void AddInstanceCtor(string constructorName)
        {
            this.writer.WriteLine("[global::System.Obsolete(\"Do not use this type directly. Use RestClient.For<" +
                this.typeModel.NamedTypeSymbol.Name + ">(...)\", true)]");
            this.writer.WriteLine("public " + constructorName + "(global::RestEase.IRequester " + this.requesterFieldName + ")");
            this.writer.WriteLine("{");
            this.writer.Indent++;

            this.writer.WriteLine("this." + this.requesterFieldName + " = requester;");

            this.writer.Indent--;
            this.writer.WriteLine("}");
        }

        private void AddStaticCtor(string constructorName)
        {
            if (this.classHeadersFieldName == null)
                return;

            this.writer.WriteLine("static " + constructorName + "()");
            this.writer.WriteLine("{");
            this.writer.Indent++;

            this.writer.WriteLine(this.qualifiedTypeName + "." + this.classHeadersFieldName +
                " = new global::System.Collections.Generic.KeyValuePair<string, string>[]");
            this.writer.WriteLine("{");
            this.writer.Indent++;
            foreach (var header in this.typeModel.HeaderAttributes)
            {
                this.writer.WriteLine("new global::System.Collections.Generic.KeyValuePair<string, string>(" +
                    QuoteString(header.Attribute.Name) + ", " + QuoteString(header.Attribute.Value) + "),");
            }
            this.writer.Indent--;
            this.writer.WriteLine("};");

            this.writer.Indent--;
            this.writer.WriteLine("}");
        }

        public EmittedProperty EmitProperty(PropertyModel propertyModel)
        {
            // Because the property is declared on the interface, we don't get any generated accessibility
            if (propertyModel.IsExplicit)
            {
                this.writer.Write(propertyModel.PropertySymbol.Type.ToDisplayString(SymbolDisplayFormats.MethodOrPropertyReturnType));
                this.writer.Write(" ");
                this.writer.Write(propertyModel.PropertySymbol.ContainingType.ToDisplayString(SymbolDisplayFormats.ImplementedInterface));
                this.writer.Write(".");
                this.writer.WriteLine(propertyModel.PropertySymbol.ToDisplayString(SymbolDisplayFormats.PropertyDeclaration));
            }
            else
            {
                this.writer.Write("public ");
                this.writer.Write(propertyModel.PropertySymbol.Type.ToDisplayString(SymbolDisplayFormats.MethodOrPropertyReturnType));
                this.writer.Write(" ");
                this.writer.WriteLine(propertyModel.PropertySymbol.ToDisplayString(SymbolDisplayFormats.PropertyDeclaration));
            }
            return new EmittedProperty(propertyModel);
        }

        public void EmitRequesterProperty(PropertyModel propertyModel)
        {
            this.writer.WriteLine("public global::RestEase.IRequester " +
                propertyModel.PropertySymbol.ToDisplayString(SymbolDisplayFormats.SymbolName) +
                " { get { return this." + this.requesterFieldName + "; } }");
        }

        public void EmitDisposeMethod(MethodModel _)
        {
            this.writer.WriteLine("void global::System.IDisposable.Dispose()");
            this.writer.WriteLine("{");
            this.writer.Indent++;
            this.writer.WriteLine("this." + this.requesterFieldName + ".Dispose();");
            this.writer.Indent--;
            this.writer.WriteLine("}");
        }

        public MethodEmitter EmitMethod(MethodModel methodModel)
        {
            return new MethodEmitter(
                methodModel,
                this.writer,
                this.wellKnownSymbols,
                this.qualifiedTypeName,
                this.requesterFieldName,
                this.classHeadersFieldName,
                this.GenerateFieldName("methodInfo_" + methodModel.MethodSymbol.Name));
        }

        public EmittedType Generate()
        {
            this.writer.Indent--;
            this.writer.WriteLine("}");
            this.writer.Indent--;
            this.writer.WriteLine("}");
            this.writer.Flush();

            var sourceText = SourceText.From(this.stringWriter.ToString(), Encoding.UTF8);
            return new EmittedType(sourceText);
        }
    }
}