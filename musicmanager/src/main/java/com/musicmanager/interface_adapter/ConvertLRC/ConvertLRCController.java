package com.musicmanager.interface_adapter.ConvertLRC;

import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputData;

import java.util.List;

import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputBoundary;

public class ConvertLRCController {
    private ConvertLRCInputBoundary interactor;
    private ConvertLRCInputData inputData;

    public ConvertLRCController(String path, List<String> formats, ConvertLRCInputBoundary interactor) {
        this.inputData = new ConvertLRCInputData(path, formats);
        this.interactor = interactor;
    }

    /* execute ConvertLRC use case */
    public void execute() {
        interactor.execute(inputData);
    }
}
