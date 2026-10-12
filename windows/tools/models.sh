#!/bin/bash
# The Windows app's models, generated afresh from the Mac's and iPhone's Swift models (the same answers of native-app.js).
cd "$(dirname "$0")/../.." && python3 windows/tools/swift2cs.py Simpl.Models windows/Simpl/Models/Models.g.cs \
  ios/SimplCourses/Native/Models.swift ios/SimplCourses/Native/CourseModels.swift mac/Simpl/Screens/DashboardModels.swift
