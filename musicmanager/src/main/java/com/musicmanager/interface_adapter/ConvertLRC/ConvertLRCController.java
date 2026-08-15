package com.musicmanager.interface_adapter.ConvertLRC;

import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputData;

import java.util.List;

import com.musicmanager.use_case.ConvertLRC.ConvertLRCInputBoundary;

public class ConvertLRCController {
    private ConvertLRCInputBoundary interactor;
    private ConvertLRCInputData inputData;

    public ConvertLRCController(ConvertLRCInputBoundary interactor) {
        this.interactor = interactor;
    }

    /* execute ConvertLRC use case */
    public void execute(String path, List<String> formats) {
        this.inputData = new ConvertLRCInputData(path, formats);
        interactor.execute(inputData);
    }
}
