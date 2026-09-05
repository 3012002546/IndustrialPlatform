namespace IndustrialPlatform.ReferenceData.Contracts;

public static class ReferenceDataPermissions
{
    public const string DictionaryView = "referencedata.dictionary.view";
    public const string DictionaryCreate = "referencedata.dictionary.create";
    public const string DictionaryUpdate = "referencedata.dictionary.update";
    public const string DictionaryPublish = "referencedata.dictionary.publish";
    public const string DictionaryDisable = "referencedata.dictionary.disable";
    public const string ParameterView = "referencedata.parameter.view";
    public const string ParameterCreate = "referencedata.parameter.create";
    public const string ParameterUpdate = "referencedata.parameter.update";
    public const string ParameterDisable = "referencedata.parameter.disable";
    public const string ParameterReadSecretReference = "referencedata.parameter.read-secret-reference";
    public const string DynamicPropertyView = "referencedata.dynamic-property.view";
    public const string DynamicPropertyCreate = "referencedata.dynamic-property.create";
    public const string DynamicPropertyUpdate = "referencedata.dynamic-property.update";
    public const string DynamicPropertyPublish = "referencedata.dynamic-property.publish";
    public const string DynamicPropertyDisable = "referencedata.dynamic-property.disable";
    public const string UnitOfMeasureView = "referencedata.unit-of-measure.view";
    public const string UnitOfMeasureCreate = "referencedata.unit-of-measure.create";
    public const string UnitOfMeasureUpdate = "referencedata.unit-of-measure.update";
    public const string UnitOfMeasurePublish = "referencedata.unit-of-measure.publish";
    public const string UnitOfMeasureDisable = "referencedata.unit-of-measure.disable";
    public const string MetadataView = "referencedata.metadata.view";
    public const string MetadataCreate = "referencedata.metadata.create";
    public const string MetadataUpdate = "referencedata.metadata.update";
    public const string MetadataPublish = "referencedata.metadata.publish";
    public const string MetadataDisable = "referencedata.metadata.disable";
    public const string CodingRuleView = "referencedata.coding-rule.view";
    public const string CodingRuleCreate = "referencedata.coding-rule.create";
    public const string CodingRuleUpdate = "referencedata.coding-rule.update";
    public const string CodingRulePublish = "referencedata.coding-rule.publish";
    public const string CodingRuleDisable = "referencedata.coding-rule.disable";
    public const string CodingRulePreview = "referencedata.coding-rule.preview";
    public const string CodingRuleGenerate = "referencedata.coding-rule.generate";
    public const string StateMachineView = "referencedata.state-machine.view";
    public const string StateMachineCreate = "referencedata.state-machine.create";
    public const string StateMachineUpdate = "referencedata.state-machine.update";
    public const string StateMachinePublish = "referencedata.state-machine.publish";
    public const string StateMachineDisable = "referencedata.state-machine.disable";
    public const string PlatformManage = "referencedata.platform.manage";

    public static IReadOnlyList<string> All { get; } = [DictionaryView, DictionaryCreate, DictionaryUpdate, DictionaryPublish, DictionaryDisable, ParameterView, ParameterCreate, ParameterUpdate, ParameterDisable, ParameterReadSecretReference, DynamicPropertyView, DynamicPropertyCreate, DynamicPropertyUpdate, DynamicPropertyPublish, DynamicPropertyDisable, UnitOfMeasureView, UnitOfMeasureCreate, UnitOfMeasureUpdate, UnitOfMeasurePublish, UnitOfMeasureDisable, MetadataView, MetadataCreate, MetadataUpdate, MetadataPublish, MetadataDisable, CodingRuleView, CodingRuleCreate, CodingRuleUpdate, CodingRulePublish, CodingRuleDisable, CodingRulePreview, CodingRuleGenerate, StateMachineView, StateMachineCreate, StateMachineUpdate, StateMachinePublish, StateMachineDisable, PlatformManage];
}
